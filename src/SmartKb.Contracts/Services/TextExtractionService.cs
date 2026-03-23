using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Extracts text from binary document formats using local libraries (no external API calls).
/// PDF: PdfPig. DOCX/PPTX/XLSX: Open XML SDK.
/// </summary>
public sealed class TextExtractionService : ITextExtractionService
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".pptx", ".xlsx",
    };

    private readonly ILogger<TextExtractionService> _logger;

    public TextExtractionService(ILogger<TextExtractionService> logger)
    {
        _logger = logger;
    }

    public bool CanExtract(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext);
    }

    public async Task<TextExtractionResult> ExtractTextAsync(
        Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            return ext switch
            {
                ".pdf" => await ExtractPdfAsync(content, cancellationToken),
                ".docx" => ExtractDocx(content),
                ".pptx" => ExtractPptx(content),
                ".xlsx" => ExtractXlsx(content),
                _ => TextExtractionResult.Failure($"Unsupported format: {ext}", ext),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Text extraction failed for {FileName}", fileName);
            return TextExtractionResult.Failure($"Extraction failed: {ex.Message}", ext);
        }
    }

    internal static Task<TextExtractionResult> ExtractPdfAsync(Stream content, CancellationToken ct)
    {
        // PdfPig is synchronous; wrap for consistency.
        using var document = PdfDocument.Open(content);
        var sb = new StringBuilder();
        var pageCount = document.NumberOfPages;

        for (var i = 1; i <= pageCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            var page = document.GetPage(i);
            var text = page.Text;

            if (!string.IsNullOrWhiteSpace(text))
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(text);
            }
        }

        var result = sb.Length > 0
            ? TextExtractionResult.Ok(sb.ToString(), pageCount, "pdf")
            : TextExtractionResult.Failure("PDF contains no extractable text (may be scanned/image-only).", "pdf");

        return Task.FromResult(result);
    }

    internal static TextExtractionResult ExtractDocx(Stream content)
    {
        using var document = WordprocessingDocument.Open(content, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return TextExtractionResult.Failure("DOCX has no document body.", "docx");

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(text);
            }
        }

        return sb.Length > 0
            ? TextExtractionResult.Ok(sb.ToString(), 1, "docx")
            : TextExtractionResult.Failure("DOCX contains no extractable text.", "docx");
    }

    internal static TextExtractionResult ExtractPptx(Stream content)
    {
        using var document = PresentationDocument.Open(content, false);
        var presentationPart = document.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList is null)
            return TextExtractionResult.Failure("PPTX has no slides.", "pptx");

        var sb = new StringBuilder();
        var slideCount = 0;

        foreach (var slideId in presentationPart.Presentation.SlideIdList
            .Elements<DocumentFormat.OpenXml.Presentation.SlideId>())
        {
            var slidePart = (SlidePart?)presentationPart.GetPartById(slideId.RelationshipId!);
            if (slidePart is null) continue;

            slideCount++;

            foreach (var paragraph in slidePart.Slide?.Descendants<DocumentFormat.OpenXml.Drawing.Paragraph>() ?? [])
            {
                var text = paragraph.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(text);
                }
            }
        }

        return sb.Length > 0
            ? TextExtractionResult.Ok(sb.ToString(), slideCount, "pptx")
            : TextExtractionResult.Failure("PPTX contains no extractable text.", "pptx");
    }

    internal static TextExtractionResult ExtractXlsx(Stream content)
    {
        using var document = SpreadsheetDocument.Open(content, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
            return TextExtractionResult.Failure("XLSX has no workbook.", "xlsx");

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();
        var sheetCount = 0;

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var sheetData = worksheetPart.Worksheet?.GetFirstChild<SheetData>();
            if (sheetData is null) continue;

            sheetCount++;

            foreach (var row in sheetData.Elements<Row>())
            {
                var cells = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var value = GetCellValue(cell, sharedStrings);
                    if (!string.IsNullOrEmpty(value))
                        cells.Add(value);
                }

                if (cells.Count > 0)
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(string.Join('\t', cells));
                }
            }
        }

        return sb.Length > 0
            ? TextExtractionResult.Ok(sb.ToString(), sheetCount, "xlsx")
            : TextExtractionResult.Failure("XLSX contains no data.", "xlsx");
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.InnerText;
        if (value is null) return string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString
            && sharedStrings is not null
            && int.TryParse(value, out var index))
        {
            var element = sharedStrings.ElementAtOrDefault(index);
            return element?.InnerText ?? value;
        }

        return value;
    }
}
