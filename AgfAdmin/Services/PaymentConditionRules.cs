using System.Globalization;
using System.Text.RegularExpressions;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // Regels van betalingscondities. Puur en deterministisch; de frontend spiegelt ze één-op-één (zelfde code, grenzen en datumregels).
    //
    // Vervaldatum (DueDate):
    //   InvoiceDate: factuurdatum + Days.
    //   EndOfMonth:  laatste kalenderdag van de factuurmaand + Days (factuur 15-02-2028, 30 dagen -> 29-02-2028 + 30 = 30-03-2028).
    // Kortingsdatum (DiscountDate): altijd factuurdatum + DiscountDays (ongeacht de basis); geen korting -> null.
    // Kalenderdagen, geen werkdagen; geen tijdzone (alleen de datum telt).
    public static class PaymentConditionRules
    {
        public const int MaxCodeLength = 16;
        public const int MaxDescriptionLength = 200;
        public const int MinDays = 0;
        public const int MaxDays = 365;
        // Korting: groter dan 0 en kleiner dan 100 procent, hooguit 2 decimalen; kortingsdagen 0..Days.
        public const decimal MaxDiscountPercent = 99.99m;

        // '%' is toegestaan voor automatisch aangemaakte kortingscodes ("30D-2%8").
        private static readonly Regex Code = new("^[A-Za-z0-9][A-Za-z0-9._%-]{0,15}$", RegexOptions.Compiled);

        public static bool IsCode(string value) => value != null && Code.IsMatch(value);
        public static string CodeKey(string code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToLowerInvariant();
        public static bool IsBasis(string value) => value != null && PaymentConditionBasis.All.Contains(value, StringComparer.Ordinal);

        // Automatisch aangemaakte conditie voor een legacy-termijn in dagen (zelfde tekst als de migratie AddPaymentConditions).
        public static string DefaultCodeForDays(int days) => $"{days}D";
        public static string DefaultDescriptionNl(int days) => days == 1 ? "1 dag na factuurdatum" : $"{days} dagen na factuurdatum";
        public static string DefaultDescriptionEn(int days) => days == 1 ? "1 day after invoice date" : $"{days} days after invoice date";

        // Vrije termijn op een rol (dagen, basis, optionele korting): de betekenis waarop PaymentConditionResolver een conditie zoekt of maakt.
        public readonly record struct Terms(int Days, string Basis, decimal? DiscountPercent, int? DiscountDays)
        {
            public static Terms InvoiceDays(int days) => new(days, PaymentConditionBasis.InvoiceDate, null, null);
            public bool Matches(PaymentCondition p) =>
                p.Days == Days && p.Basis == Basis && p.DiscountPercent == DiscountPercent && p.DiscountDays == DiscountDays;
        }

        // Code van een automatisch aangemaakte conditie: "{n}D" (factuurdatum) of "{n}EM" (einde maand), met korting "-{p}%{d}":
        // "30D", "30EM", "30D-2%8", "60EM-2.5%10". Langste vorm "365EM-99.99%365" = 15 tekens (max 16).
        public static string DefaultCode(Terms terms)
        {
            var code = $"{terms.Days}{(terms.Basis == PaymentConditionBasis.EndOfMonth ? "EM" : "D")}";
            return terms.DiscountPercent is { } percent && terms.DiscountDays is { } discountDays ? $"{code}-{PercentText(percent, CultureInfo.InvariantCulture)}%{discountDays}" : code;
        }

        public static string DefaultDescriptionNl(Terms terms)
        {
            var text = terms.Basis == PaymentConditionBasis.EndOfMonth
                ? (terms.Days == 1 ? "1 dag na einde maand" : $"{terms.Days} dagen na einde maand")
                : DefaultDescriptionNl(terms.Days);
            return terms.DiscountPercent is { } percent && terms.DiscountDays is { } discountDays
                ? $"{text}, {PercentText(percent, Dutch)}% korting bij betaling binnen {discountDays} {(discountDays == 1 ? "dag" : "dagen")}"
                : text;
        }

        public static string DefaultDescriptionEn(Terms terms)
        {
            var text = terms.Basis == PaymentConditionBasis.EndOfMonth
                ? (terms.Days == 1 ? "1 day after end of month" : $"{terms.Days} days after end of month")
                : DefaultDescriptionEn(terms.Days);
            return terms.DiscountPercent is { } percent && terms.DiscountDays is { } discountDays
                ? $"{text}, {PercentText(percent, CultureInfo.InvariantCulture)}% discount if paid within {discountDays} {(discountDays == 1 ? "day" : "days")}"
                : text;
        }

        private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

        // 2.00 -> "2", 2.50 -> "2.5" (nl: "2,5"): geen overbodige nullen.
        private static string PercentText(decimal percent, CultureInfo culture) => percent.ToString("0.##", culture);

        // Vrije termijn van een rol valideren (relatieformulier): dagen leeg (= standaard) of 0..365, korting beide of geen van beide,
        // percentage groter dan 0 en hooguit 99,99 met 2 decimalen, kortingsdagen 0..effectieve termijn. Zelfde grenzen als Validate.
        public static void ValidateTerms(int? days, int effectiveDays, decimal? discountPercent, int? discountDays, string prefix, Dictionary<string, List<string>> errors)
        {
            if (days is < MinDays or > MaxDays) ValidationErrors.Add(errors, $"{prefix}.paymentTermsDays", $"De betalingstermijn ligt tussen {MinDays} en {MaxDays} dagen (of leeg voor de standaard).");
            if (discountPercent != null && discountDays == null) ValidationErrors.Add(errors, $"{prefix}.discountDays", "Vul ook in binnen hoeveel dagen de korting geldt, of laat beide kortingsvelden leeg.");
            if (discountPercent == null && discountDays != null) ValidationErrors.Add(errors, $"{prefix}.discountPercent", "Vul ook het kortingspercentage in, of laat beide kortingsvelden leeg.");
            if (discountPercent != null && (discountPercent <= 0m || discountPercent > MaxDiscountPercent || decimal.Round(discountPercent.Value, 2) != discountPercent.Value))
            {
                ValidationErrors.Add(errors, $"{prefix}.discountPercent", "Het kortingspercentage ligt tussen 0 en 100 (exclusief) met hooguit 2 decimalen.");
            }
            if (discountDays != null && (discountDays < 0 || discountDays > effectiveDays))
            {
                ValidationErrors.Add(errors, $"{prefix}.discountDays", $"Kortingsdagen liggen tussen 0 en de betalingstermijn ({effectiveDays} dagen).");
            }
        }

        public static DateOnly DueDate(DateOnly invoiceDate, int days, string basis) =>
            basis == PaymentConditionBasis.EndOfMonth
                ? new DateOnly(invoiceDate.Year, invoiceDate.Month, DateTime.DaysInMonth(invoiceDate.Year, invoiceDate.Month)).AddDays(days)
                : invoiceDate.AddDays(days);

        public static DateOnly DueDate(DateOnly invoiceDate, PaymentCondition condition) => DueDate(invoiceDate, condition.Days, condition.Basis);

        public static DateOnly? DiscountDate(DateOnly invoiceDate, PaymentCondition condition) =>
            condition.DiscountPercent == null || condition.DiscountDays == null ? null : invoiceDate.AddDays(condition.DiscountDays.Value);

        // Variant voor UTC-middernacht (opslag van inkoopfacturen): alleen de kalenderdag telt, uitkomst weer UTC-middernacht.
        public static DateTime DueDate(DateTime invoiceDateUtc, int days, string basis)
        {
            var due = DueDate(DateOnly.FromDateTime(invoiceDateUtc), days, basis);
            return DateTime.SpecifyKind(due.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }

        // Validatie van de inhoud (code, omschrijvingen, dagen, basis, korting). Sleutels in camelCase zoals de API ze toont.
        public static void Validate(string code, string descriptionNl, string descriptionEn, int? days, string basis, decimal? discountPercent, int? discountDays,
            Dictionary<string, List<string>> errors)
        {
            if (code == null) ValidationErrors.Add(errors, "code", "Een code is verplicht.");
            else if (!IsCode(code)) ValidationErrors.Add(errors, "code", $"Een code bestaat uit letters, cijfers, '.', '-', '_' of '%' (max {MaxCodeLength}), zonder spaties.");
            if (descriptionNl == null) ValidationErrors.Add(errors, "descriptionNl", "Een Nederlandse omschrijving is verplicht.");
            if (descriptionEn == null) ValidationErrors.Add(errors, "descriptionEn", "Een Engelse omschrijving is verplicht.");
            if (days == null || days < MinDays || days > MaxDays) ValidationErrors.Add(errors, "days", $"Het aantal dagen ligt tussen {MinDays} en {MaxDays}.");
            if (!IsBasis(basis)) ValidationErrors.Add(errors, "basis", $"Basis moet een van deze waarden zijn: {string.Join(", ", PaymentConditionBasis.All)}.");
            if ((discountPercent == null) != (discountDays == null))
            {
                ValidationErrors.Add(errors, "discountPercent", "Vul kortingspercentage en kortingsdagen allebei in, of geen van beide.");
            }
            if (discountPercent != null && (discountPercent <= 0m || discountPercent > MaxDiscountPercent || decimal.Round(discountPercent.Value, 2) != discountPercent.Value))
            {
                ValidationErrors.Add(errors, "discountPercent", "Het kortingspercentage ligt tussen 0 en 100 (exclusief) met hooguit 2 decimalen.");
            }
            if (discountDays != null && (discountDays < 0 || (days != null && discountDays > days)))
            {
                ValidationErrors.Add(errors, "discountDays", "Kortingsdagen liggen tussen 0 en het aantal dagen van de betalingstermijn.");
            }
        }
    }
}
