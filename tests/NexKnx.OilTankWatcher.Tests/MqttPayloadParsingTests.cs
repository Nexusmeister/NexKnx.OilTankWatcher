using NexKnx.OilTankWatcher.Mqtt;

namespace NexKnx.OilTankWatcher.Tests;

public class MqttPayloadParsingTests
{
    [Theory]
    [InlineData("67.3", 67.3)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("  42.5  ", 42.5)]
    public void TryParsePercent_ParsesPlainNumericPayload(string payload, double expected)
    {
        var result = MqttOilLevelClient.TryParsePercent(payload, out var percent);

        Assert.True(result);
        Assert.Equal(expected, percent);
    }

    [Fact]
    public void TryParsePercent_ParsesJsonObjectWithValueProperty()
    {
        var result = MqttOilLevelClient.TryParsePercent("{\"value\": 55.2}", out var percent);

        Assert.True(result);
        Assert.Equal(55.2, percent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("{\"other\": 5}")]
    public void TryParsePercent_FailsForUnparsablePayload(string payload)
    {
        var result = MqttOilLevelClient.TryParsePercent(payload, out _);

        Assert.False(result);
    }
}
