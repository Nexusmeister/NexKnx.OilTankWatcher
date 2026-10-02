using System.Net;
using Knx.Falcon;
using Knx.Falcon.ApplicationData.DatapointTypes;
using Knx.Falcon.Configuration;
using Knx.Falcon.Sdk;
using Microsoft.Extensions.Logging;
using NexKnx.OilTankWatcher.Configuration;

namespace NexKnx.OilTankWatcher.Knx;

/// <summary>
/// KNX-Anbindung über das Falcon SDK (Knx.Falcon.Sdk 6.x).
///
/// API-Stand gegen die tatsächlich installierte Paketversion verifiziert
/// (per Reflection), nicht aus dem Gedächtnis übernommen:
///   - Knx.Falcon.Sdk.KnxBus(ConnectorParameters) + ConnectAsync/WriteGroupValueAsync
///   - Knx.Falcon.Configuration.IpTunnelingConnectorParameters /
///     IpRoutingConnectorParameters, inkl. ConnectorParameters.AutoReconnect
///   - Knx.Falcon.ApplicationData.DatapointTypes: DptFactory.Default.Get(5,1)
///     liefert die DPT_Scaling-Implementierung für DPT 5.001 (Prozent 0..100%,
///     Rundtrip-getestet: 42% -> Byte 107); DptFactory.Default.Get(12,1200)
///     liefert "DPT_VolumeLiquid_Litre" (Liter, 4-Byte-Ganzzahl, Rundtrip-
///     getestet: 6688 L -> exakt 6688 L zurück, ohne die Rundungsungenauigkeit,
///     die ein 2-Byte-Gleitkomma-DPT 9.xxx bei Werten in dieser Größenordnung
///     hätte); Dpt9 für die (fraktionale) Restreichweite in Tagen und Dpt1 für
///     Warn- und Störungsbit (DPT 1.005 "Alarm").
///     Hinweis: Alle DPT-1.x- bzw. DPT-12.x-Subtypen sind auf dem Draht
///     dieselbe 1-Bit- bzw. 4-Byte-Ganzzahl-Kodierung - die Subtyp-Nummer ist
///     reine ETS-Projektmetadaten (Anzeige/Interpretation der Gruppenadresse)
///     und wird nicht mitübertragen.
/// </summary>
public sealed class FalconKnxGateway : IKnxGateway
{
    private readonly KnxOptions _options;
    private readonly ILogger<FalconKnxGateway> _logger;
    private readonly KnxBus _bus;

    private readonly DptBase _percentDpt = DptFactory.Default.Get(5, 1); // DPT 5.001 "Scaling"
    private readonly DptBase _volumeDpt = DptFactory.Default.Get(12, 1200); // DPT 12.1200 "VolumeLiquid_Litre"
    private readonly Dpt9 _runtimeDpt = new(); // DPT 9.xxx (fraktionale Restreichweite in Tagen)
    private readonly Dpt1 _alarmDpt = new(); // DPT 1.005 "Alarm" (Warn- und Störungsbit)

    private readonly GroupAddress _percentGa;
    private readonly GroupAddress _litersGa;
    private readonly GroupAddress _warningGa;
    private readonly GroupAddress _faultGa;
    private readonly GroupAddress _runtimeGa;

    public FalconKnxGateway(KnxOptions options, ILogger<FalconKnxGateway> logger)
    {
        _options = options;
        _logger = logger;

        _percentGa = GroupAddress.Parse(options.GroupAddresses.PercentLevel);
        _litersGa = GroupAddress.Parse(options.GroupAddresses.LitersLevel);
        _warningGa = GroupAddress.Parse(options.GroupAddresses.WarningBit);
        _faultGa = GroupAddress.Parse(options.GroupAddresses.FaultBit);
        _runtimeGa = GroupAddress.Parse(options.GroupAddresses.RuntimeDays);

        var connectorParameters = BuildConnectorParameters(options);
        connectorParameters.AutoReconnect = true;

        _bus = new KnxBus(connectorParameters);
        _bus.ConnectionStateChanged += (_, _) =>
            _logger.LogInformation("KNX-Verbindungsstatus: {State}", _bus.ConnectionState);
    }

    private static ConnectorParameters BuildConnectorParameters(KnxOptions options) => options.ConnectionType switch
    {
        KnxConnectionType.Tunneling => new IpTunnelingConnectorParameters(
            options.Tunneling.Host,
            options.Tunneling.Port,
            useNat: options.Tunneling.UseNat),

        KnxConnectionType.Routing => new IpRoutingConnectorParameters(
            IPAddress.Parse(options.Routing.MulticastAddress)),

        _ => throw new NotSupportedException($"Unbekannter KNX-Verbindungstyp: {options.ConnectionType}")
    };

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _bus.ConnectAsync(cancellationToken);
        _logger.LogInformation(
            "KNX-Bus verbunden ({ConnectionType}), Status: {State}.",
            _options.ConnectionType,
            _bus.ConnectionState);
    }

    public Task WritePercentAsync(double percent, CancellationToken cancellationToken) =>
        WriteAsync(_percentGa, _percentDpt.ToGroupValue((float)percent), cancellationToken);

    public Task WriteLitersAsync(double liters, CancellationToken cancellationToken) =>
        WriteAsync(_litersGa, _volumeDpt.ToGroupValue((uint)Math.Round(liters)), cancellationToken);

    public Task WriteWarningAsync(bool warning, CancellationToken cancellationToken) =>
        WriteAsync(_warningGa, _alarmDpt.ToGroupValue(warning), cancellationToken);

    public Task WriteFaultAsync(bool fault, CancellationToken cancellationToken) =>
        WriteAsync(_faultGa, _alarmDpt.ToGroupValue(fault), cancellationToken);

    public Task WriteRuntimeDaysAsync(double days, CancellationToken cancellationToken) =>
        WriteAsync(_runtimeGa, _runtimeDpt.ToGroupValue((float)days), cancellationToken);

    private async Task WriteAsync(GroupAddress address, GroupValue value, CancellationToken cancellationToken)
    {
        try
        {
            await _bus.WriteGroupValueAsync(address, value, MessagePriority.Low, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KNX-Schreibvorgang auf Gruppenadresse {Address} fehlgeschlagen.", address);
        }
    }

    public async ValueTask DisposeAsync() => await _bus.DisposeAsync();
}
