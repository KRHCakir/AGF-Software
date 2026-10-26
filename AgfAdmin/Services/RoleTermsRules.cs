using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // Rolafspraken van klant en leverancier: betaalwijze, leveringsconditie, handelsvoorwaarden, valuta en (alleen klant) krediet.
    // Pure normalisatie + validatie; de frontend spiegelt dezelfde regels:
    //   - betaalwijze leeg -> BankTransfer; DirectDebit vraagt mandaatkenmerk (max 35) en ondertekeningsdatum (niet in de toekomst);
    //     bij elke andere betaalwijze worden de mandaatvelden leeggemaakt;
    //   - leveringsconditie: geen (alles leeg) | Incoterm (Incoterms 2020-code + plaats verplicht) | Franco / AfKwekerij (toelichting
    //     optioneel) | Other (omschrijving verplicht); velden die bij de gekozen soort niet horen worden leeggemaakt;
    //   - handelsvoorwaarden: geen | Own | Frugiventa | Cofreurop | Other (+ omschrijving verplicht; anders leeg);
    //   - krediet: limiet leeg of >= 0 (2 decimalen); verzekerd -> verzekeraar (Other + naam) en verzekerde limiet verplicht;
    //     niet verzekerd -> alle verzekeringsvelden leeg. Een verlopen verzekering is een waarschuwing, geen fout.
    public static class RoleTermsRules
    {
        public static readonly IReadOnlyList<string> Incoterms2020 = new[] { "EXW", "FCA", "CPT", "CIP", "DAP", "DPU", "DDP", "FAS", "FOB", "CFR", "CIF" };

        public const int MaxMandateReferenceLength = 35;
        public const int MaxIncotermPlaceLength = 100;
        public const int MaxDeliveryTermTextLength = 200;
        public const int MaxTradeConditionsTextLength = 200;
        public const int MaxInsurerTextLength = 100;
        public const int MaxInsurerReferenceLength = 64;
        public const decimal MaxAmount = 9_999_999_999_999_999.99m;
        public const int InsuranceExpiryWarningDays = 30;

        public sealed record Terms(string PaymentMethod, string MandateReference, DateOnly? MandateSignedOn, string DeliveryTermKind, string Incoterm,
            string IncotermPlace, string DeliveryTermText, string TradeConditions, string TradeConditionsText);

        public sealed record Credit(decimal? CreditLimit, bool IsInsured, string Insurer, string InsurerText, decimal? InsuredLimit, string InsurerReference, DateOnly? InsuredUntil);

        public static Terms NormalizeTerms(Terms raw, string prefix, DateOnly today, Dictionary<string, List<string>> errors)
        {
            var method = Blank(raw.PaymentMethod) ?? PaymentMethods.BankTransfer;
            if (!PaymentMethods.All.Contains(method, StringComparer.Ordinal))
            {
                ValidationErrors.Add(errors, $"{prefix}.paymentMethod", $"Betaalwijze moet een van deze waarden zijn: {string.Join(", ", PaymentMethods.All)}.");
            }
            string mandate = null;
            DateOnly? signedOn = null;
            if (method == PaymentMethods.DirectDebit)
            {
                mandate = ValidationErrors.OptionalText(raw.MandateReference, MaxMandateReferenceLength, $"{prefix}.mandateReference", errors);
                if (mandate == null && !errors.ContainsKey($"{prefix}.mandateReference")) ValidationErrors.Add(errors, $"{prefix}.mandateReference", "Bij automatische incasso is het mandaatkenmerk verplicht.");
                signedOn = raw.MandateSignedOn;
                if (signedOn == null && !errors.ContainsKey($"{prefix}.mandateSignedOn")) ValidationErrors.Add(errors, $"{prefix}.mandateSignedOn", "Bij automatische incasso is de datum van ondertekening van het mandaat verplicht.");
                else if (signedOn > today) ValidationErrors.Add(errors, $"{prefix}.mandateSignedOn", "De ondertekeningsdatum van het mandaat kan niet in de toekomst liggen.");
            }

            var kind = Blank(raw.DeliveryTermKind);
            string incoterm = null, place = null, deliveryText = null;
            if (kind != null && !DeliveryTermKinds.All.Contains(kind, StringComparer.Ordinal))
            {
                ValidationErrors.Add(errors, $"{prefix}.deliveryTermKind", $"Leveringsconditie moet leeg zijn of een van: {string.Join(", ", DeliveryTermKinds.All)}.");
            }
            else if (kind == DeliveryTermKinds.Incoterm)
            {
                incoterm = Blank(raw.Incoterm)?.ToUpperInvariant();
                if (incoterm == null || !Incoterms2020.Contains(incoterm, StringComparer.Ordinal))
                {
                    ValidationErrors.Add(errors, $"{prefix}.incoterm", $"Kies een Incoterm 2020: {string.Join(", ", Incoterms2020)}.");
                }
                place = ValidationErrors.OptionalText(raw.IncotermPlace, MaxIncotermPlaceLength, $"{prefix}.incotermPlace", errors);
                if (place == null && !errors.ContainsKey($"{prefix}.incotermPlace")) ValidationErrors.Add(errors, $"{prefix}.incotermPlace", "Een Incoterm hoort bij een plaats (bijvoorbeeld FCA Venlo).");
            }
            else if (kind == DeliveryTermKinds.Other)
            {
                deliveryText = ValidationErrors.OptionalText(raw.DeliveryTermText, MaxDeliveryTermTextLength, $"{prefix}.deliveryTermText", errors);
                if (deliveryText == null && !errors.ContainsKey($"{prefix}.deliveryTermText")) ValidationErrors.Add(errors, $"{prefix}.deliveryTermText", "Omschrijf de eigen leveringsconditie.");
            }
            else if (kind != null)
            {
                deliveryText = ValidationErrors.OptionalText(raw.DeliveryTermText, MaxDeliveryTermTextLength, $"{prefix}.deliveryTermText", errors);
            }

            var trade = Blank(raw.TradeConditions);
            string tradeText = null;
            if (trade != null && !TradeConditionKinds.All.Contains(trade, StringComparer.Ordinal))
            {
                ValidationErrors.Add(errors, $"{prefix}.tradeConditions", $"Handelsvoorwaarden moeten leeg zijn of een van: {string.Join(", ", TradeConditionKinds.All)}.");
            }
            else if (trade == TradeConditionKinds.Other)
            {
                tradeText = ValidationErrors.OptionalText(raw.TradeConditionsText, MaxTradeConditionsTextLength, $"{prefix}.tradeConditionsText", errors);
                if (tradeText == null && !errors.ContainsKey($"{prefix}.tradeConditionsText")) ValidationErrors.Add(errors, $"{prefix}.tradeConditionsText", "Omschrijf welke andere handelsvoorwaarden gelden.");
            }
            return new Terms(method, mandate, signedOn, kind, incoterm, place, deliveryText, trade, tradeText);
        }

        public static Credit NormalizeCredit(Credit raw, string prefix, Dictionary<string, List<string>> errors)
        {
            if (raw.CreditLimit != null && !IsAmount(raw.CreditLimit.Value))
            {
                ValidationErrors.Add(errors, $"{prefix}.creditLimit", "De kredietlimiet is leeg of een bedrag van 0 of meer met hooguit 2 decimalen.");
            }
            if (!raw.IsInsured)
            {
                return new Credit(raw.CreditLimit, false, null, null, null, null, null);
            }
            var insurer = Blank(raw.Insurer);
            string insurerText = null;
            if (insurer == null || !CreditInsurers.All.Contains(insurer, StringComparer.Ordinal))
            {
                ValidationErrors.Add(errors, $"{prefix}.insurer", $"Kies de kredietverzekeraar: {string.Join(", ", CreditInsurers.All)}.");
            }
            else if (insurer == CreditInsurers.Other)
            {
                insurerText = ValidationErrors.OptionalText(raw.InsurerText, MaxInsurerTextLength, $"{prefix}.insurerText", errors);
                if (insurerText == null && !errors.ContainsKey($"{prefix}.insurerText")) ValidationErrors.Add(errors, $"{prefix}.insurerText", "Vul de naam van de kredietverzekeraar in.");
            }
            if (raw.InsuredLimit == null || !IsAmount(raw.InsuredLimit.Value))
            {
                ValidationErrors.Add(errors, $"{prefix}.insuredLimit", "De verzekerde limiet is verplicht bij een kredietverzekering: een bedrag van 0 of meer met hooguit 2 decimalen.");
            }
            var reference = ValidationErrors.OptionalText(raw.InsurerReference, MaxInsurerReferenceLength, $"{prefix}.insurerReference", errors);
            return new Credit(raw.CreditLimit, true, insurer, insurerText, raw.InsuredLimit, reference, raw.InsuredUntil);
        }

        public static bool IsAmount(decimal value) => value >= 0m && value <= MaxAmount && decimal.Round(value, 2) == value;

        // Verzekering verlopen (einddatum vóór vandaag) of verloopt binnen 30 dagen (vandaag t/m vandaag + 30).
        public static bool InsuranceExpired(DateOnly? insuredUntil, DateOnly today) => insuredUntil != null && insuredUntil < today;
        public static bool InsuranceExpiresSoon(DateOnly? insuredUntil, DateOnly today) =>
            insuredUntil != null && insuredUntil >= today && insuredUntil <= today.AddDays(InsuranceExpiryWarningDays);

        // Gat tussen interne limiet en verzekerde limiet (alleen als de verzekerde limiet lager is), anders null.
        public static decimal? InsuranceGap(decimal? creditLimit, bool isInsured, decimal? insuredLimit) =>
            isInsured && creditLimit != null && insuredLimit != null && insuredLimit < creditLimit ? creditLimit - insuredLimit : null;

        public static bool IsCurrency(string value) => PurchaseInvoiceRules.IsCurrencyCode(value);

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
