namespace AgfAdmin.Services
{
    // Kalenderdatums voor bedrijfsregels, altijd in Europe/Amsterdam (ongeacht de tijdzone van de server). Eén implementatie,
    // gedeeld door services en de centrale TraceabilityGuard. Tijdstippen (RecordedAt, ConsumedAt) blijven echte UTC-tijden.
    public interface IBusinessCalendar
    {
        // Vandaag als kalenderdatum in Europe/Amsterdam.
        DateOnly Today();

        // De kalenderdatum in Europe/Amsterdam van een UTC-tijdstip (bijvoorbeeld SalesDispatch.DispatchedAt).
        DateOnly DateOf(DateTime utc);
    }

    public sealed class AmsterdamBusinessCalendar : IBusinessCalendar
    {
        public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

        private readonly TimeProvider _clock;

        public AmsterdamBusinessCalendar(TimeProvider clock)
        {
            _clock = clock ?? TimeProvider.System;
        }

        public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Zone).DateTime);

        public DateOnly DateOf(DateTime utc) => ToAmsterdamDate(utc);

        // Gedeelde omrekening; een DateTime zonder soort komt uit de database en is UTC.
        public static DateOnly ToAmsterdamDate(DateTime utc) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone));
    }
}
