using AgfAdmin.Services;

namespace AgfAdmin.Tests.Infrastructure;

// Vaste "vandaag" voor tests die niet over de tijdzone gaan.
public sealed class FixedBusinessCalendar : IBusinessCalendar
{
    private readonly DateOnly _today;
    public FixedBusinessCalendar(DateOnly today) => _today = today;
    public DateOnly Today() => _today;
}

// Vaste UTC-klok voor de tijdzonetests van AmsterdamBusinessCalendar.
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;
    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
    public override DateTimeOffset GetUtcNow() => _utcNow;
}
