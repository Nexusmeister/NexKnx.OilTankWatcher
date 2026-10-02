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

    /// Füllstand (%), bei dem das Warnbit gesetzt wird.
    public double WarningThresholdPercent { get; set; } = 25;

    /// Füllstand (%), ab dem das Warnbit (Hysterese) wieder zurückgenommen wird.
    public double WarningRecoveryPercent { get; set; } = 28;

    /// Zeit ohne gültigen Messwert, nach der das Störungsbit gesetzt wird.
    public double FaultAfterHours { get; set; } = 12;

    /// Zyklisches Sendeintervall auf den KNX-Bus, unabhängig von Änderungen.
    public int SendIntervalMinutes { get; set; } = 10;

    /// Mindeständerung des Füllstands (%), die ein sofortiges Senden auslöst.
    public double ChangeThresholdPercent { get; set; } = 0.5;
}

public sealed class KnxTunnelingOptions
{
    public string Host { get; set; } = "192.168.178.21";
    public int Port { get; set; } = 3671;
    public bool UseNat { get; set; } = false;
}

public sealed class KnxRoutingOptions
{
    public string MulticastAddress { get; set; } = "224.0.23.12";
}

public sealed class KnxGroupAddressOptions
{
    public string PercentLevel { get; set; } = "10/3/0";
    public string LitersLevel { get; set; } = "10/3/1";
    public string WarningBit { get; set; } = "10/3/2";
    public string FaultBit { get; set; } = "10/3/2";
    public string RuntimeDays { get; set; } = "10/3/1";
}
