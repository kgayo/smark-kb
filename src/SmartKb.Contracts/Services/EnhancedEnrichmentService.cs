using System.Text.RegularExpressions;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Phase 2 enhanced enrichment (v2): extends baseline with weighted category scoring,
/// technology/framework detection, and component/module extraction from text content.
/// </summary>
public sealed partial class EnhancedEnrichmentService : IEnrichmentService
{
    public const int CurrentEnrichmentVersion = 2;

    private readonly BaselineEnrichmentService _baseline = new();

    public EnrichmentResult Enrich(CanonicalRecord record)
    {
        // Start with baseline enrichment for error tokens, PII, environment, and severity.
        var baseline = _baseline.Enrich(record);

        var text = $"{record.Title}\n{record.TextContent}";

        // Enhanced: weighted category with confidence score.
        var (category, categoryConfidence) = DetectCategoryWeighted(text, record);

        // Enhanced: technology/framework detection.
        var technologyTags = DetectTechnologyTags(text);

        // Enhanced: component/module extraction.
        var component = ExtractComponent(text);

        // Enhanced: product area inference when record has none.
        var productArea = baseline.ProductArea ?? InferProductArea(text, technologyTags);

        return baseline with
        {
            Category = category ?? baseline.Category,
            CategoryConfidence = categoryConfidence,
            ProductArea = productArea,
            TechnologyTags = technologyTags,
            Component = component,
            EnrichmentVersion = CurrentEnrichmentVersion,
        };
    }

    // --- Weighted Category Detection ---

    internal static (string? Category, float Confidence) DetectCategoryWeighted(string text, CanonicalRecord record)
    {
        var lower = text.ToLowerInvariant();

        // Wiki pages are always documentation.
        if (record.SourceType is Enums.SourceType.WikiPage)
            return ("documentation", 1.0f);

        // Score each category by number of matching signals.
        var scores = new Dictionary<string, int>
        {
            ["bug"] = CountMatches(lower, "bug", "defect", "regression", "broken", "crash",
                "exception", "error", "failure", "stack trace", "null reference", "unhandled"),
            ["feature_request"] = CountMatches(lower, "feature request", "enhancement",
                "improvement", "new feature", "add support", "would be nice", "wish list"),
            ["incident"] = CountMatches(lower, "incident", "outage", "downtime", "degradation",
                "p1", "sev1", "sev-1", "service disruption", "customer impact", "availability"),
            ["question"] = CountMatches(lower, "question", "how to", "how do", "help me",
                "guidance", "clarification", "what is", "can i", "is it possible"),
            ["task"] = CountMatches(lower, "task", "action item", "todo", "to-do",
                "assign", "work item"),
            ["documentation"] = CountMatches(lower, "documentation", "wiki", "guide",
                "tutorial", "readme", "how-to guide", "reference doc"),
        };

        var best = scores
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .FirstOrDefault();

        if (best.Value == 0)
            return (null, 0f);

        // Confidence: proportion of signals matched out of max possible, capped at 1.0.
        var totalSignals = scores.Values.Sum();
        var confidence = Math.Min(1.0f, best.Value / (float)Math.Max(totalSignals, 1));

        return (best.Key, confidence);
    }

    private static int CountMatches(string text, params string[] terms)
    {
        var count = 0;
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    // --- Technology/Framework Detection ---

    private static readonly (string Tag, string[] Patterns)[] TechnologyPatterns =
    [
        ("Azure SQL", ["azure sql", "sql database", "sqlazure"]),
        ("Azure Blob Storage", ["blob storage", "azure blob", "blobserviceclient"]),
        ("Azure Service Bus", ["service bus", "servicebus", "sbclient"]),
        ("Azure Key Vault", ["key vault", "keyvault", "secretclient"]),
        ("Azure AI Search", ["azure search", "cognitive search", "ai search", "searchindexclient"]),
        ("Azure DevOps", ["azure devops", "ado", "dev.azure.com"]),
        ("Azure Functions", ["azure function", "function app", "functionapp"]),
        ("Azure App Service", ["app service", "appservice", "webapp"]),
        (".NET", [".net", "dotnet", "csharp", "c#", "asp.net", "aspnet"]),
        ("Entity Framework", ["entity framework", "efcore", "ef core", "dbcontext"]),
        ("React", ["react", "jsx", "tsx", "usestate", "useeffect"]),
        ("TypeScript", ["typescript", ".ts", ".tsx"]),
        ("Node.js", ["node.js", "nodejs", "npm", "express"]),
        ("Python", ["python", "pip install", "django", "flask"]),
        ("Terraform", ["terraform", "tfstate", ".tf"]),
        ("Docker", ["docker", "dockerfile", "container image"]),
        ("Kubernetes", ["kubernetes", "k8s", "kubectl", "helm"]),
        ("SQL Server", ["sql server", "mssql", "sqlcmd", "t-sql"]),
        ("Redis", ["redis", "rediscache"]),
        ("RabbitMQ", ["rabbitmq", "amqp"]),
        ("Entra ID", ["entra id", "azure ad", "aad", "msal", "microsoft identity"]),
        ("SharePoint", ["sharepoint", "graph api", "onedrive"]),
        ("HubSpot", ["hubspot"]),
        ("ClickUp", ["clickup"]),
    ];

    internal static IReadOnlyList<string> DetectTechnologyTags(string text)
    {
        var lower = text.ToLowerInvariant();
        var tags = new List<string>();

        foreach (var (tag, patterns) in TechnologyPatterns)
        {
            foreach (var pattern in patterns)
            {
                if (lower.Contains(pattern, StringComparison.Ordinal))
                {
                    tags.Add(tag);
                    break;
                }
            }
        }

        return tags;
    }

    // --- Component/Module Extraction ---

    internal static string? ExtractComponent(string text)
    {
        // Look for explicit component/module references in common formats.
        var match = ComponentPatternRegex().Match(text);
        if (match.Success)
        {
            var component = match.Groups[1].Value.Trim();
            // Limit length and clean up.
            if (component.Length > 0 && component.Length <= 100)
                return component;
        }

        // Look for namespace-style references (e.g., SmartKb.Api, MyApp.Services.Auth).
        var nsMatch = NamespacePatternRegex().Match(text);
        if (nsMatch.Success)
            return nsMatch.Value;

        return null;
    }

    // --- Product Area Inference ---

    private static readonly (string Area, string[] Keywords)[] ProductAreaPatterns =
    [
        ("Authentication", ["authentication", "auth", "login", "sso", "msal", "entra", "token", "oauth"]),
        ("Networking", ["network", "dns", "firewall", "vpn", "proxy", "load balancer", "ssl", "tls"]),
        ("Storage", ["storage", "blob", "disk", "file share", "backup"]),
        ("Database", ["database", "sql", "migration", "schema", "query", "index", "deadlock"]),
        ("API", ["api", "endpoint", "rest", "graphql", "swagger", "openapi"]),
        ("CI/CD", ["pipeline", "ci/cd", "build", "deploy", "release", "github actions"]),
        ("Monitoring", ["monitoring", "alerting", "logging", "metrics", "traces", "observability"]),
        ("Security", ["security", "vulnerability", "rbac", "permission", "acl", "encryption"]),
        ("Frontend", ["frontend", "ui", "ux", "react", "css", "browser", "rendering"]),
        ("Infrastructure", ["infrastructure", "terraform", "arm template", "iac", "provisioning"]),
        ("Ingestion", ["ingestion", "connector", "webhook", "sync", "import"]),
    ];

    internal static string? InferProductArea(string text, IReadOnlyList<string> technologyTags)
    {
        var lower = text.ToLowerInvariant();
        var bestArea = (Name: (string?)null, Score: 0);

        foreach (var (area, keywords) in ProductAreaPatterns)
        {
            var score = 0;
            foreach (var kw in keywords)
            {
                if (lower.Contains(kw, StringComparison.Ordinal))
                    score++;
            }
            if (score > bestArea.Score)
                bestArea = (area, score);
        }

        // Require at least 2 matching keywords to infer with confidence.
        if (bestArea.Score >= 2)
            return bestArea.Name;

        return null;
    }

    // Matches patterns like "Component: Auth Service", "Module: Ingestion", "[component] API Gateway".
    [GeneratedRegex(@"(?:component|module|service|subsystem)\s*[:=\]]\s*(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ComponentPatternRegex();

    // Matches .NET namespace-style references like SmartKb.Api.Controllers.
    [GeneratedRegex(@"\b[A-Z][a-zA-Z]+(?:\.[A-Z][a-zA-Z]+){2,}\b", RegexOptions.Compiled)]
    private static partial Regex NamespacePatternRegex();
}
