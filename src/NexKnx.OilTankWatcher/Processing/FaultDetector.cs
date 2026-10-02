namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Erkennt den Störungsfall "seit mehr als X Stunden kein gültiger Messwert".
/// Der Referenzzeitpunkt wird beim Start auf den Startzeitpunkt des Dienstes
/// gesetzt, damit auch ein kompletter Ausfall der Datenquelle seit dem Start
/// erkannt wird.
/// </summary>
public sealed class FaultDetector
{
    private readonly TimeSpan _maxAge;
    private DateTimeOffset _lastValidAt;

    public FaultDetector(TimeSpan maxAge, DateTimeOffset startedAt)
    {
        _maxAge = maxAge;
        _lastValidAt = startedAt;
    }

    public void MarkValid(DateTimeOffset now) => _lastValidAt = now;

    public bool IsFaulty(DateTimeOffset now) => now - _lastValidAt > _maxAge;
}
