using SmartKb.Contracts.Connectors;

namespace SmartKb.Contracts.Tests;

public sealed class GraphApiConstantsTests
{
    [Fact]
    public void BaseUrl_IsGraphV1Endpoint()
    {
        Assert.Equal("https://graph.microsoft.com/v1.0", GraphApiConstants.BaseUrl);
    }

    [Fact]
    public void TokenUrl_IsEntraOAuth2Endpoint()
    {
        Assert.Equal("https://login.microsoftonline.com/{0}/oauth2/v2.0/token", GraphApiConstants.TokenUrl);
    }

    [Fact]
    public void TokenUrl_SupportsStringFormat()
    {
        var tenantId = "my-tenant-id";
        var result = string.Format(GraphApiConstants.TokenUrl, tenantId);
        Assert.Equal($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token", result);
    }
}
