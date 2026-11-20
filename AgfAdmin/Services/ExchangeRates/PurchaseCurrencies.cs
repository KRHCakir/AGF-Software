using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Valuta-ronde: de korte lijst valuta's waaruit je kiest bij een inkooporder en inkoopfactuur, aan te vullen onder Instellingen (Beheer).
    // EUR staat er altijd in en vooraan. Alleen valuta's die de ECB publiceert krijgen automatisch een koers; voor de rest vult de gebruiker
    // de koers zelf in. Opgeslagen als komma-lijst in CompanySettings.PurchaseCurrencies.
    public static class PurchaseCurrencyRules
    {
        public const string DefaultList = "EUR,USD,GBP,CHF,TRY,ZAR,CAD,NOK";
        public const int MaxCount = 20;
        public const int MaxStoredLength = 100;

        // De valuta's met een dagelijkse ECB-referentiekoers (stand november 2026). Alleen om bij het kiezen te tonen of er een automatische
        // koers komt; het ophalen zelf vraagt het altijd aan de ECB.
        public static readonly IReadOnlySet<string> EcbCurrencies = new HashSet<string>(StringComparer.Ordinal)
        {
            "USD", "JPY", "BGN", "CZK", "DKK", "GBP", "HUF", "PLN", "RON", "SEK", "CHF", "ISK", "NOK", "TRY", "AUD", "BRL", "CAD", "CNY", "HKD",
            "IDR", "ILS", "INR", "KRW", "MXN", "MYR", "NZD", "PHP", "SGD", "THB", "ZAR",
        };

        public static IReadOnlyList<string> Parse(string stored)
        {
            var codes = (string.IsNullOrWhiteSpace(stored) ? DefaultList : stored)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(code => code.ToUpperInvariant())
                .Where(PurchaseInvoiceRules.IsCurrencyCode)
                .Where(code => code != ExchangeRateRules.Euro)
                .Distinct()
                .ToList();
            codes.Insert(0, ExchangeRateRules.Euro);
            return codes;
        }
    }

    public sealed class PurchaseCurrencyDto
    {
        public string Code { get; set; }
        // De ECB publiceert een dagkoers voor deze valuta (bij EUR niet van toepassing).
        public bool HasEcbRate { get; set; }
    }

    public sealed class PurchaseCurrencySettingsDto
    {
        public List<PurchaseCurrencyDto> Currencies { get; set; } = new();
    }

    public sealed class UpdatePurchaseCurrenciesDto
    {
        public List<string> Codes { get; set; }
    }

    public sealed class PurchaseCurrencyService(AppDbContext db)
    {
        public async Task<PurchaseCurrencySettingsDto> GetAsync(CancellationToken cancellationToken = default)
        {
            var stored = await db.CompanySettings.AsNoTracking().OrderBy(s => s.Id).Select(s => s.PurchaseCurrencies).FirstOrDefaultAsync(cancellationToken);
            return ToDto(PurchaseCurrencyRules.Parse(stored));
        }

        public async Task<ServiceResult<PurchaseCurrencySettingsDto>> UpdateAsync(UpdatePurchaseCurrenciesDto request, CancellationToken cancellationToken = default)
        {
            var codes = (request?.Codes ?? new List<string>()).Select(code => code?.Trim().ToUpperInvariant() ?? "").ToList();
            var invalid = codes.Where(code => !PurchaseInvoiceRules.IsCurrencyCode(code)).ToList();
            if (request?.Codes == null || invalid.Count > 0)
            {
                return ServiceResult<PurchaseCurrencySettingsDto>.Validation(new Dictionary<string, string[]>
                {
                    ["codes"] = new[] { "Elke valuta is een ISO-code van drie letters (bijvoorbeeld USD)." }
                });
            }
            var list = PurchaseCurrencyRules.Parse(string.Join(',', codes));
            if (list.Count > PurchaseCurrencyRules.MaxCount)
            {
                return ServiceResult<PurchaseCurrencySettingsDto>.Validation(new Dictionary<string, string[]>
                {
                    ["codes"] = new[] { $"Hoogstens {PurchaseCurrencyRules.MaxCount} valuta's." }
                });
            }
            var settings = await db.CompanySettings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (settings == null) db.CompanySettings.Add(settings = new CompanySettings());
            settings.PurchaseCurrencies = string.Join(',', list);
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<PurchaseCurrencySettingsDto>.Ok(ToDto(list));
        }

        private static PurchaseCurrencySettingsDto ToDto(IEnumerable<string> codes) => new()
        {
            Currencies = codes.Select(code => new PurchaseCurrencyDto { Code = code, HasEcbRate = PurchaseCurrencyRules.EcbCurrencies.Contains(code) }).ToList()
        };
    }
}
