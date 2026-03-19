using System.Text.RegularExpressions;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Enrichment service (v2): keyword-based severity/environment extraction, regex-based error token
/// and PII detection (from v1 baseline), plus weighted category scoring, technology/framework
/// detection, and component/module extraction.
/// </summary>
public sealed partial class EnhancedEnrichmentService : IEnrichmentService
{
    public const int CurrentEnrichmentVersion = 2;

    public EnrichmentResult Enrich(CanonicalRecord record)
    {
        var text = $"{record.Title}\n{record.TextContent}";

        // Baseline enrichment: severity, environment, error tokens, PII.
        var severity = record.Severity ?? DetectSeverity(text);
        var environment = DetectEnvironment(text);
        var errorTokens = ExtractErrorTokens(text);
        var piiFlags = DetectPii(text);

        // Enhanced: weighted category with confidence score.
        var (category, categoryConfidence) = DetectCategoryWeighted(text, record);

        // Enhanced: technology/framework detection.
        var technologyTags = DetectTechnologyTags(text);

        // Enhanced: component/module extraction.
        var component = ExtractComponent(text);

        // Enhanced: product area inference when record has none.
        var productArea = record.ProductArea ?? InferProductArea(text, technologyTags);

        return new EnrichmentResult
        {
            Category = category,
            CategoryConfidence = categoryConfidence,
            ProductArea = productArea,
            Severity = severity,
            Environment = environment,
            ErrorTokens = errorTokens,
            PiiFlags = piiFlags,
            TechnologyTags = technologyTags,
            Component = component,
            EnrichmentVersion = CurrentEnrichmentVersion,
        };
    }

    // --- Severity Detection (from baseline v1) ---

    private static string? DetectSeverity(string text)
    {
        var lower = text.ToLowerInvariant();

        if (ContainsAny(lower, "critical", "sev-1", "sev1", "p0", "p1", "severity: critical", "priority: 1"))
            return "critical";
        if (ContainsAny(lower, "high", "sev-2", "sev2", "p2", "severity: high", "priority: 2"))
            return "high";
        if (ContainsAny(lower, "medium", "sev-3", "sev3", "p3", "severity: medium", "priority: 3"))
            return "medium";
        if (ContainsAny(lower, "low", "sev-4", "sev4", "p4", "severity: low", "priority: 4", "minor"))
            return "low";

        return null;
    }

    // --- Environment Detection (from baseline v1) ---

    private static string? DetectEnvironment(string text)
    {
        var lower = text.ToLowerInvariant();

        if (ContainsAny(lower, "production", "prod environment", "in prod", "prod instance"))
            return "production";
        if (ContainsAny(lower, "staging", "stage environment", "pre-prod", "preprod"))
            return "staging";
        if (ContainsAny(lower, "development", "dev environment", "dev instance", "local dev"))
            return "development";
        if (ContainsAny(lower, "test environment", "qa environment", "uat", "testing"))
            return "test";

        return null;
    }

    // --- Error Token Extraction (from baseline v1) ---

    internal static IReadOnlyList<string> ExtractErrorTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // .NET/Java exception class names (e.g., NullReferenceException, IOException).
        foreach (Match m in ExceptionNameRegex().Matches(text))
            tokens.Add(m.Value);

        // HTTP status codes (e.g., 400, 401, 403, 404, 500, 502, 503).
        foreach (Match m in HttpStatusCodeRegex().Matches(text))
            tokens.Add($"HTTP {m.Groups[1].Value}");

        // Error codes (e.g., ERR-001, ERROR_CODE_123, AADSTS50076).
        foreach (Match m in ErrorCodeRegex().Matches(text))
            tokens.Add(m.Value);

        // Hex error codes (e.g., 0x80070005).
        foreach (Match m in HexErrorRegex().Matches(text))
            tokens.Add(m.Value);

        return tokens.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // --- PII Detection (from baseline v1) ---

    internal static IReadOnlyList<string> DetectPii(string text)
    {
        var flags = new List<string>();

        if (EmailRegex().IsMatch(text))
            flags.Add("email");
        if (PhoneRegex().IsMatch(text))
            flags.Add("phone");
        if (SsnRegex().IsMatch(text))
            flags.Add("ssn");
        if (CreditCardRegex().IsMatch(text))
            flags.Add("credit_card");

        return flags;
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
            ["feature_request"] = CountMatches(lower, "enhancement",
                "improvement", "new feature", "add support", "would be nice", "wish list", "feature request"),
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

    // --- Shared Helpers ---

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.Ordinal))
                return true;
        }
        return false;
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

    // --- Regex Patterns (from baseline v1) ---

    // Exception names: PascalCase words ending with Exception, Error, Fault.
    [GeneratedRegex(@"\b[A-Z][a-zA-Z]+(?:Exception|Error|Fault)\b", RegexOptions.Compiled)]
    private static partial Regex ExceptionNameRegex();

    // HTTP status codes in common patterns like "HTTP 500", "status 404", "returned 503".
    [GeneratedRegex(@"(?:HTTP|status|returned|response|code)\s*([45]\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HttpStatusCodeRegex();

    // Error codes like ERR-001, ERROR_CODE_123, AADSTS50076.
    [GeneratedRegex(@"\b(?:ERR[-_]?\d+|ERROR[-_][A-Z_]+\d*|[A-Z]{2,}STS\d+|E\d{4,})\b", RegexOptions.Compiled)]
    private static partial Regex ErrorCodeRegex();

    // Hex error codes like 0x80070005.
    [GeneratedRegex(@"\b0x[0-9A-Fa-f]{4,}\b", RegexOptions.Compiled)]
    private static partial Regex HexErrorRegex();

    // Email addresses.
    [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    // Phone numbers (US-style: 10+ digits with optional separators).
    [GeneratedRegex(@"(?<!\d)(?:\+?1[-.\s]?)?(?:\(?\d{3}\)?[-.\s]?)?\d{3}[-.\s]?\d{4}(?!\d)", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    // SSN pattern (xxx-xx-xxxx).
    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex SsnRegex();

    // Credit card numbers (13-19 digits with optional separators).
    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3,4}\d{1,4}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();

    // Matches patterns like "Component: Auth Service", "Module: Ingestion", "[component] API Gateway".
    [GeneratedRegex(@"(?:component|module|service|subsystem)\s*[:=\]]\s*(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ComponentPatternRegex();

    // Matches .NET namespace-style references like SmartKb.Api.Controllers.
    [GeneratedRegex(@"\b[A-Z][a-zA-Z]+(?:\.[A-Z][a-zA-Z]+){2,}\b", RegexOptions.Compiled)]
    private static partial Regex NamespacePatternRegex();
}
