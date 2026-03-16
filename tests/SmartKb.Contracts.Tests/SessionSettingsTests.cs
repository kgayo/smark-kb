using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Tests;

public class SessionSettingsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var settings = new SessionSettings();
        Assert.Equal(24, settings.DefaultExpiryHours);
        Assert.Equal(200, settings.MaxMessagesPerSession);
        Assert.Equal(50, settings.MaxActiveSessionsPerUser);
        Assert.True(settings.HasExpiry);
    }

    [Fact]
    public void HasExpiry_ReturnsFalse_WhenZero()
    {
        var settings = new SessionSettings { DefaultExpiryHours = 0 };
        Assert.False(settings.HasExpiry);
    }

    [Fact]
    public void HasExpiry_ReturnsTrue_WhenPositive()
    {
        var settings = new SessionSettings { DefaultExpiryHours = 48 };
        Assert.True(settings.HasExpiry);
    }

    [Fact]
    public void CustomValues_AreRetained()
    {
        var settings = new SessionSettings
        {
            DefaultExpiryHours = 72,
            MaxMessagesPerSession = 500,
            MaxActiveSessionsPerUser = 100,
        };
        Assert.Equal(72, settings.DefaultExpiryHours);
        Assert.Equal(500, settings.MaxMessagesPerSession);
        Assert.Equal(100, settings.MaxActiveSessionsPerUser);
    }
}
