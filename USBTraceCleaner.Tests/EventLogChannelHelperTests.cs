using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class EventLogChannelHelperTests
{
    [Theory]
    [InlineData("The specified channel could not be found", true)]
    [InlineData("Не удалось найти указанный канал", true)]
    [InlineData("Error 16000", true)]
    [InlineData("ok", false)]
    [InlineData(null, false)]
    public void IsChannelNotFound_DetectsMissingChannel(string? message, bool expected)
    {
        Assert.Equal(expected, EventLogChannelHelper.IsChannelNotFound(message));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Exists_ReturnsFalseForEmptyChannel(string channel)
    {
        Assert.False(EventLogChannelHelper.Exists(channel));
    }
}
