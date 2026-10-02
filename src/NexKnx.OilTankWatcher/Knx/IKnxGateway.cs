namespace NexKnx.OilTankWatcher.Knx;

public interface IKnxGateway : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task WritePercentAsync(double percent, CancellationToken cancellationToken);

    Task WriteLitersAsync(double liters, CancellationToken cancellationToken);

    Task WriteWarningAsync(bool warning, CancellationToken cancellationToken);

    Task WriteFaultAsync(bool fault, CancellationToken cancellationToken);

    Task WriteRuntimeDaysAsync(double days, CancellationToken cancellationToken);
}
