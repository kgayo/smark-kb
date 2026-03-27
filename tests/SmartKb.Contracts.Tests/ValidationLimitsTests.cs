using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class ValidationLimitsTests
{
    [Fact]
    public void ConnectorNameMaxLength_Is256()
    {
        Assert.Equal(256, ValidationLimits.ConnectorNameMaxLength);
    }

    [Fact]
    public void StopWordMaxLength_Is128()
    {
        Assert.Equal(128, ValidationLimits.StopWordMaxLength);
    }

    [Fact]
    public void SpecialTokenMaxLength_Is256()
    {
        Assert.Equal(256, ValidationLimits.SpecialTokenMaxLength);
    }

    [Fact]
    public void SynonymRuleMaxLength_Is1024()
    {
        Assert.Equal(1024, ValidationLimits.SynonymRuleMaxLength);
    }

    [Fact]
    public void BoostFactorRange_Is1To10()
    {
        Assert.Equal(1, ValidationLimits.BoostFactorMin);
        Assert.Equal(10, ValidationLimits.BoostFactorMax);
    }
}
