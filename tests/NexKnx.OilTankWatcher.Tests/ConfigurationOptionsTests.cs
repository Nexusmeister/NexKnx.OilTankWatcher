using NexKnx.OilTankWatcher.Configuration;

namespace NexKnx.OilTankWatcher.Tests;

public class KnxOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new KnxOptions();

        options.ConnectionType.ShouldBe(KnxConnectionType.Tunneling);
        options.Tunneling.ShouldNotBeNull();
        options.Routing.ShouldNotBeNull();
        options.GroupAddresses.ShouldNotBeNull();
        options.WarningThresholdPercent.ShouldBe(25);
        options.WarningRecoveryPercent.ShouldBe(28);
        options.FaultAfterHours.ShouldBe(12);
        options.SendIntervalMinutes.ShouldBe(10);
        options.ChangeThresholdPercent.ShouldBe(0.5);
    }
}

public class KnxTunnelingOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new KnxTunnelingOptions();

        options.Host.ShouldBe(string.Empty);
        options.Port.ShouldBe(3671);
        options.UseNat.ShouldBeFalse();
    }
}

public class KnxRoutingOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new KnxRoutingOptions();

        options.MulticastAddress.ShouldBe("224.0.23.12");
    }
}

public class KnxGroupAddressOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new KnxGroupAddressOptions();

        options.PercentLevel.ShouldBe("10/3/0");
        // Note: LitersLevel/RuntimeDays and WarningBit/FaultBit each share
        // the same default group address in KnxOptions.cs. This looks like
        // a pre-existing copy-paste collision, but is deliberately only
        // captured here as current behavior (characterization), not
        // "fixed" - changing the production defaults doesn't belong in
        // this coverage-focused change. If this is indeed unintentional, a
        // separate ticket should be filed for it.
        options.LitersLevel.ShouldBe("10/3/1");
        options.WarningBit.ShouldBe("10/3/2");
        options.FaultBit.ShouldBe("10/3/2");
        options.RuntimeDays.ShouldBe("10/3/1");
    }
}

public class MqttOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new MqttOptions();

        options.Host.ShouldBe("localhost");
        options.Port.ShouldBe(1883);
        options.Topic.ShouldBe("oiltank/main/value");
        options.ClientId.ShouldBe("oiltank-knx-bridge");
        options.Username.ShouldBeNull();
        options.Password.ShouldBeNull();
        options.ReconnectDelaySeconds.ShouldBe(5);
        options.MaxReconnectDelaySeconds.ShouldBe(60);
    }
}

public class TankOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new TankOptions();

        options.NominalVolumeLiters.ShouldBe(9100);
        options.MaxStepPercent.ShouldBe(5);
        options.MedianWindowSize.ShouldBe(5);
    }
}

public class RuntimeEstimationOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new RuntimeEstimationOptions();

        options.Enabled.ShouldBeTrue();
        options.HistoryDays.ShouldBe(30);
        options.MinimumSamples.ShouldBe(5);
        options.DatabasePath.ShouldBe("history.db");
    }
}
