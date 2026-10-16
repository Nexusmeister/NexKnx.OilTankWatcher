namespace NexKnx.OilTankWatcher.Configuration;

public enum KnxConnectionType
{
    Tunneling,
    Routing
}

public sealed class KnxOptions
{
    public const string SectionName = "Knx";

    public KnxConnectionType ConnectionType { get; set; } = KnxConnectionType.Tunneling;
    public KnxTunnelingOptions Tunneling { get; set; } = new();
    public KnxRoutingOptions Routing { get; set; } = new();
    public KnxGroupAddressOptions GroupAddresses { get; set; } = new();

    /// Fill level (%) at which the warning bit is set.
    public double WarningThresholdPercent { get; set; } = 25;

    /// Fill level (%) above which the warning bit (hysteresis) is cleared again.
    public double WarningRecoveryPercent { get; set; } = 28;

    /// Time without a valid reading after which the fault bit is set.
    public double FaultAfterHours { get; set; } = 12;

    /// Cyclic send interval onto the KNX bus, independent of changes.
    public int SendIntervalMinutes { get; set; } = 10;

    /// Minimum change in fill level (%) that triggers an immediate send.
    public double ChangeThresholdPercent { get; set; } = 0.5;
}

public sealed class KnxTunnelingOptions
{
    // No default: the IP of the KNX/IP interface is environment-specific
    // and is set exclusively via appsettings.json or Knx__Tunneling__Host
    // (see README).
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3671;
    public bool UseNat { get; set; } = false;
}

public sealed class KnxRoutingOptions
{
    // NOSONAR (S1313): official multicast address fixed by the KNX
    // specification for KNXnet/IP routing - not an environment-specific value.
    public string MulticastAddress { get; set; } = "224.0.23.12"; // NOSONAR
}

public sealed class KnxGroupAddressOptions
{
    public string PercentLevel { get; set; } = "10/3/0";
    public string LitersLevel { get; set; } = "10/3/1";
    public string WarningBit { get; set; } = "10/3/2";
    public string FaultBit { get; set; } = "10/3/2";
    public string RuntimeDays { get; set; } = "10/3/1";
}
