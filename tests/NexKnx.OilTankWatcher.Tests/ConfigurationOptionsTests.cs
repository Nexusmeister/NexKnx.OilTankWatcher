using NexKnx.OilTankWatcher.Configuration;

namespace NexKnx.OilTankWatcher.Tests;

public class KnxOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new KnxOptions();

        Assert.Equal(KnxConnectionType.Tunneling, options.ConnectionType);
        Assert.NotNull(options.Tunneling);
        Assert.NotNull(options.Routing);
        Assert.NotNull(options.GroupAddresses);
        Assert.Equal(25, options.WarningThresholdPercent);
        Assert.Equal(28, options.WarningRecoveryPercent);
        Assert.Equal(12, options.FaultAfterHours);
        Assert.Equal(10, options.SendIntervalMinutes);
        Assert.Equal(0.5, options.ChangeThresholdPercent);
    }
}

public class KnxTunnelingOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new KnxTunnelingOptions();

        Assert.Equal("192.168.178.21", options.Host);
        Assert.Equal(3671, options.Port);
        Assert.False(options.UseNat);
    }
}

public class KnxRoutingOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new KnxRoutingOptions();

        Assert.Equal("224.0.23.12", options.MulticastAddress);
    }
}

public class KnxGroupAddressOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new KnxGroupAddressOptions();

        Assert.Equal("10/3/0", options.PercentLevel);
        // Hinweis: LitersLevel/RuntimeDays und WarningBit/FaultBit teilen sich
        // in KnxOptions.cs jeweils dieselbe Default-Gruppenadresse. Das sieht
        // nach einer vorbestehenden Copy-Paste-Kollision aus, wird hier aber
        // bewusst nur als aktuelles Verhalten festgehalten (characterization),
        // nicht "repariert" - eine Änderung der Produktions-Defaults gehört
        // nicht in dieses Coverage-fokussierte Change. Falls das tatsächlich
        // unbeabsichtigt ist, sollte dafür ein eigenes Ticket angelegt werden.
        Assert.Equal("10/3/1", options.LitersLevel);
        Assert.Equal("10/3/2", options.WarningBit);
        Assert.Equal("10/3/2", options.FaultBit);
        Assert.Equal("10/3/1", options.RuntimeDays);
    }
}

public class MqttOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new MqttOptions();

        Assert.Equal("localhost", options.Host);
        Assert.Equal(1883, options.Port);
        Assert.Equal("oiltank/main/value", options.Topic);
        Assert.Equal("oiltank-knx-bridge", options.ClientId);
        Assert.Null(options.Username);
        Assert.Null(options.Password);
        Assert.Equal(5, options.ReconnectDelaySeconds);
        Assert.Equal(60, options.MaxReconnectDelaySeconds);
    }
}

public class TankOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new TankOptions();

        Assert.Equal(9100, options.NominalVolumeLiters);
        Assert.Equal(5, options.MaxStepPercent);
        Assert.Equal(5, options.MedianWindowSize);
    }
}

public class RuntimeEstimationOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new RuntimeEstimationOptions();

        Assert.True(options.Enabled);
        Assert.Equal(30, options.HistoryDays);
        Assert.Equal(5, options.MinimumSamples);
        Assert.Equal("history.db", options.DatabasePath);
    }
}
