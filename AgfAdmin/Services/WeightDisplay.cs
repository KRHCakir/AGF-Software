using System.Globalization;
using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Gewichten tonen (fixes 5a): totalen van gewichten staan op schermen en documenten als hele kilo's, of met één decimaal als het
    // bedrijf dat instelt. Afronden gebeurt alleen bij het tonen (halve kilo's omhoog: 2.272,5 wordt 2.273); de opslag houdt drie
    // decimalen en verandert nooit. Op een document is het totaal de som van de getoonde regels, zodat het op papier optelt.
    public static class WeightDisplay
    {
        public const int DefaultDecimals = 0;
        public const int MaxDecimals = 1;
        private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

        public static bool IsAllowed(int decimals) => decimals is >= 0 and <= MaxDecimals;
        public static int Clamp(int decimals) => IsAllowed(decimals) ? decimals : DefaultDecimals;

        public static decimal Round(decimal kg, int decimals) => Math.Round(kg, Clamp(decimals), MidpointRounding.AwayFromZero);

        // decimals null: een document van vóór deze instelling; dat houdt zijn oude weergave (tot drie decimalen, niet afgerond).
        public static string Kg(decimal? kg, int? decimals)
        {
            if (kg == null) return "–";
            if (decimals == null) return kg.Value.ToString("#,##0.###", Dutch);
            return Round(kg.Value, decimals.Value).ToString(Clamp(decimals.Value) == 0 ? "#,##0" : "#,##0.0", Dutch);
        }

        // Het totaal zoals het op papier optelt: de som van de afgeronde regels (regels zonder gewicht tellen niet mee).
        public static decimal? DisplayedTotal(IEnumerable<decimal?> rows, int decimals)
        {
            var shown = rows.Where(kg => kg != null).Select(kg => Round(kg.Value, decimals)).ToList();
            return shown.Count == 0 ? null : shown.Sum();
        }
    }

    public sealed class WeightDisplaySettingsDto
    {
        // Nullable: een verzoek zonder dit veld is onvolledig en wijzigt niets (het wordt nooit stil 0).
        public int? Decimals { get; set; }
    }

    // De instelling per bedrijf onder Beheer: gewichten tonen met 0 of 1 decimaal (standaard 0).
    public sealed class WeightDisplayService(AppDbContext db)
    {
        public async Task<WeightDisplaySettingsDto> GetAsync(CancellationToken cancellationToken = default)
        {
            var settings = await db.CompanySettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            return new WeightDisplaySettingsDto { Decimals = WeightDisplay.Clamp(settings?.WeightDisplayDecimals ?? WeightDisplay.DefaultDecimals) };
        }

        // Geldt voor schermen meteen en voor nieuwe versies van documenten; een bestaande versie houdt haar eigen weergave.
        public async Task<ServiceResult<WeightDisplaySettingsDto>> UpdateAsync(WeightDisplaySettingsDto request, CancellationToken cancellationToken = default)
        {
            if (request?.Decimals is not int decimals || !WeightDisplay.IsAllowed(decimals))
            {
                return ServiceResult<WeightDisplaySettingsDto>.Validation(new Dictionary<string, string[]> { ["decimals"] = new[] { $"Decimals must be 0 or {WeightDisplay.MaxDecimals}." } });
            }
            var settings = await db.CompanySettings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (settings == null) db.CompanySettings.Add(settings = new CompanySettings());
            settings.WeightDisplayDecimals = decimals;
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<WeightDisplaySettingsDto>.Ok(new WeightDisplaySettingsDto { Decimals = decimals });
        }
    }
}
