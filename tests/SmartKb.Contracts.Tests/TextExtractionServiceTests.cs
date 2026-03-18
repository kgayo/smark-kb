using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using SmartKb.Contracts.Services;
using UglyToad.PdfPig.Writer;
using Paragraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using Cell = DocumentFormat.OpenXml.Spreadsheet.Cell;
using Row = DocumentFormat.OpenXml.Spreadsheet.Row;

namespace SmartKb.Contracts.Tests;

public class TextExtractionServiceTests
{
    private readonly TextExtractionService _service = new(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<TextExtractionService>.Instance);

    // --- CanExtract ---

    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("document.docx", true)]
    [InlineData("slides.pptx", true)]
    [InlineData("data.xlsx", true)]
    [InlineData("readme.md", false)]
    [InlineData("notes.txt", false)]
    [InlineData("config.json", false)]
    [InlineData("image.png", false)]
    [InlineData("", false)]
    public void CanExtract_ReturnsExpected(string fileName, bool expected)
    {
        Assert.Equal(expected, _service.CanExtract(fileName));
    }

    // --- PDF extraction ---

    [Fact]
    public async Task ExtractText_Pdf_ExtractsPageContent()
    {
        using var stream = CreateTestPdf("Hello from page one.", "Second page content.");

        var result = await _service.ExtractTextAsync(stream, "test.pdf");

        Assert.True(result.Success);
        Assert.Contains("Hello from page one", result.Text);
        Assert.Contains("Second page content", result.Text);
        Assert.Equal(2, result.PageCount);
        Assert.Equal("pdf", result.Format);
    }

    [Fact]
    public async Task ExtractText_Pdf_EmptyDocument_ReturnsFailure()
    {
        // Create a PDF with no text content (just empty pages).
        using var stream = CreateTestPdf();

        var result = await _service.ExtractTextAsync(stream, "empty.pdf");

        Assert.False(result.Success);
        Assert.Contains("no extractable text", result.Error);
    }

    // --- DOCX extraction ---

    [Fact]
    public async Task ExtractText_Docx_ExtractsParagraphs()
    {
        using var stream = CreateTestDocx("First paragraph.", "Second paragraph with details.");

        var result = await _service.ExtractTextAsync(stream, "doc.docx");

        Assert.True(result.Success);
        Assert.Contains("First paragraph", result.Text);
        Assert.Contains("Second paragraph with details", result.Text);
        Assert.Equal("docx", result.Format);
    }

    [Fact]
    public async Task ExtractText_Docx_EmptyDocument_ReturnsFailure()
    {
        using var stream = CreateTestDocx();

        var result = await _service.ExtractTextAsync(stream, "empty.docx");

        Assert.False(result.Success);
        Assert.Contains("no extractable text", result.Error);
    }

    // --- PPTX extraction ---

    [Fact]
    public async Task ExtractText_Pptx_ExtractsSlideText()
    {
        using var stream = CreateTestPptx("Slide 1 Title", "Slide 2 Body");

        var result = await _service.ExtractTextAsync(stream, "deck.pptx");

        Assert.True(result.Success);
        Assert.Contains("Slide 1 Title", result.Text);
        Assert.Contains("Slide 2 Body", result.Text);
        Assert.Equal(2, result.PageCount);
        Assert.Equal("pptx", result.Format);
    }

    // --- XLSX extraction ---

    [Fact]
    public async Task ExtractText_Xlsx_ExtractsSheetData()
    {
        using var stream = CreateTestXlsx(new[]
        {
            new[] { "Name", "Value" },
            new[] { "CPU", "95%" },
            new[] { "Memory", "8GB" },
        });

        var result = await _service.ExtractTextAsync(stream, "data.xlsx");

        Assert.True(result.Success);
        Assert.Contains("Name", result.Text);
        Assert.Contains("CPU", result.Text);
        Assert.Contains("95%", result.Text);
        Assert.Contains("Memory", result.Text);
        Assert.Equal("xlsx", result.Format);
    }

    // --- Unsupported format ---

    [Fact]
    public async Task ExtractText_UnsupportedFormat_ReturnsFailure()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var result = await _service.ExtractTextAsync(stream, "file.rtf");

        Assert.False(result.Success);
        Assert.Contains("Unsupported format", result.Error);
    }

    // --- Corrupt file ---

    [Fact]
    public async Task ExtractText_CorruptPdf_ReturnsFailure()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a real pdf"));

        var result = await _service.ExtractTextAsync(stream, "corrupt.pdf");

        Assert.False(result.Success);
        Assert.Contains("Extraction failed", result.Error);
    }

    [Fact]
    public async Task ExtractText_CorruptDocx_ReturnsFailure()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a real docx"));

        var result = await _service.ExtractTextAsync(stream, "corrupt.docx");

        Assert.False(result.Success);
        Assert.Contains("Extraction failed", result.Error);
    }

    // --- TextExtractionResult factory methods ---

    [Fact]
    public void TextExtractionResult_Ok_SetsFields()
    {
        var result = TextExtractionResult.Ok("content", 3, "pdf");

        Assert.True(result.Success);
        Assert.Equal("content", result.Text);
        Assert.Equal(3, result.PageCount);
        Assert.Equal("pdf", result.Format);
        Assert.Null(result.Error);
    }

    [Fact]
    public void TextExtractionResult_Failure_SetsFields()
    {
        var result = TextExtractionResult.Failure("bad file", "docx");

        Assert.False(result.Success);
        Assert.Equal("bad file", result.Error);
        Assert.Equal("docx", result.Format);
        Assert.Equal(string.Empty, result.Text);
    }

    // --- Helpers to create test documents ---

    private static MemoryStream CreateTestPdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();

        if (pageTexts.Length == 0)
        {
            // Empty page — no text.
            builder.AddPage(595, 842);
        }
        else
        {
            var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
            foreach (var text in pageTexts)
            {
                var page = builder.AddPage(595, 842);
                page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(72, 700), font);
            }
        }

        var bytes = builder.Build();
        return new MemoryStream(bytes);
    }

    private static MemoryStream CreateTestDocx(params string[] paragraphs)
    {
        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            foreach (var text in paragraphs)
            {
                mainPart.Document.Body!.Append(
                    new Paragraph(new Run(new Text(text))));
            }

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateTestPptx(params string[] slideTexts)
    {
        var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new DocumentFormat.OpenXml.Presentation.Presentation(
                new DocumentFormat.OpenXml.Presentation.SlideIdList());

            uint slideId = 256;
            foreach (var text in slideTexts)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = new DocumentFormat.OpenXml.Presentation.Slide(
                    new DocumentFormat.OpenXml.Presentation.CommonSlideData(
                        new DocumentFormat.OpenXml.Presentation.ShapeTree(
                            new DocumentFormat.OpenXml.Presentation.NonVisualGroupShapeProperties(
                                new DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties { Id = 1, Name = "" },
                                new DocumentFormat.OpenXml.Presentation.NonVisualGroupShapeDrawingProperties(),
                                new DocumentFormat.OpenXml.Presentation.ApplicationNonVisualDrawingProperties()),
                            new DocumentFormat.OpenXml.Presentation.GroupShapeProperties(),
                            new DocumentFormat.OpenXml.Presentation.Shape(
                                new DocumentFormat.OpenXml.Presentation.NonVisualShapeProperties(
                                    new DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties { Id = 2, Name = "TextBox" },
                                    new DocumentFormat.OpenXml.Presentation.NonVisualShapeDrawingProperties(),
                                    new DocumentFormat.OpenXml.Presentation.ApplicationNonVisualDrawingProperties()),
                                new DocumentFormat.OpenXml.Presentation.ShapeProperties(),
                                new DocumentFormat.OpenXml.Presentation.TextBody(
                                    new DocumentFormat.OpenXml.Drawing.BodyProperties(),
                                    new DocumentFormat.OpenXml.Drawing.Paragraph(
                                        new DocumentFormat.OpenXml.Drawing.Run(
                                            new DocumentFormat.OpenXml.Drawing.Text(text))))))));

                var relId = presentationPart.GetIdOfPart(slidePart);
                presentationPart.Presentation.SlideIdList!.Append(
                    new DocumentFormat.OpenXml.Presentation.SlideId { Id = slideId++, RelationshipId = relId });
            }

            presentationPart.Presentation.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateTestXlsx(string[][] rows)
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook(new Sheets(
                new Sheet { Id = "rId1", SheetId = 1, Name = "Sheet1" }));

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>("rId1");
            var sheetData = new SheetData();

            foreach (var rowData in rows)
            {
                var row = new Row();
                foreach (var cellValue in rowData)
                {
                    row.Append(new Cell
                    {
                        DataType = CellValues.String,
                        CellValue = new CellValue(cellValue),
                    });
                }
                sheetData.Append(row);
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }

        stream.Position = 0;
        return stream;
    }
}
