namespace AgfAdmin.Services
{
    // "Vandaag" als kalenderdatum voor bedrijfsregels, altijd in Europe/Amsterdam (ongeacht de tijdzone van de server).
    // Tijdstippen (RecordedAt, ConsumedAt) blijven echte UTC-tijden; alleen kalenderdatums zoals ShippedOn gebruiken dit.
    public interface IBusinessCalendar
    {
        DateOnly Today();
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
    }
}
