using AgfAdmin.DTOs;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // De leveringsconditie: geen (alles leeg) | Incoterm (Incoterms 2020-code + plaats verplicht) | Franco / AfKwekerij (toelichting
    // optioneel) | Other (omschrijving verplicht); velden die bij de gekozen soort niet horen worden leeggemaakt. Gedeeld door de rol van
    // klant en leverancier (RoleTermsRules) en, sinds feedback 22 november, de verkoop- en inkooporder.
    public static class DeliveryTermRules
    {
        public sealed record DeliveryTerm(string Kind, string Incoterm, string Place, string Text)
        {
            public static readonly DeliveryTerm None = new(null, null, null, null);
        }

        public static DeliveryTerm Normalize(DeliveryTerm raw, string prefix, Dictionary<string, List<string>> errors)
        {
            var kind = Blank(raw.Kind);
            string incoterm = null, place = null, text = null;
            if (kind != null && !DeliveryTermKinds.All.Contains(kind, StringComparer.Ordinal))
            {
                ValidationErrors.Add(errors, $"{prefix}.deliveryTermKind", $"Leveringsconditie moet leeg zijn of een van: {string.Join(", ", DeliveryTermKinds.All)}.");
            }
            else if (kind == DeliveryTermKinds.Incoterm)
            {
                incoterm = Blank(raw.Incoterm)?.ToUpperInvariant();
                if (incoterm == null || !RoleTermsRules.Incoterms2020.Contains(incoterm, StringComparer.Ordinal))
                {
                    ValidationErrors.Add(errors, $"{prefix}.incoterm", $"Kies een Incoterm 2020: {string.Join(", ", RoleTermsRules.Incoterms2020)}.");
                }
                place = ValidationErrors.OptionalText(raw.Place, RoleTermsRules.MaxIncotermPlaceLength, $"{prefix}.incotermPlace", errors);
                if (place == null && !errors.ContainsKey($"{prefix}.incotermPlace")) ValidationErrors.Add(errors, $"{prefix}.incotermPlace", "Een Incoterm hoort bij een plaats (bijvoorbeeld FCA Venlo).");
            }
            else if (kind == DeliveryTermKinds.Other)
            {
                text = ValidationErrors.OptionalText(raw.Text, RoleTermsRules.MaxDeliveryTermTextLength, $"{prefix}.deliveryTermText", errors);
                if (text == null && !errors.ContainsKey($"{prefix}.deliveryTermText")) ValidationErrors.Add(errors, $"{prefix}.deliveryTermText", "Omschrijf de eigen leveringsconditie.");
            }
            else if (kind != null)
            {
                text = ValidationErrors.OptionalText(raw.Text, RoleTermsRules.MaxDeliveryTermTextLength, $"{prefix}.deliveryTermText", errors);
            }
            return new DeliveryTerm(kind, incoterm, place, text);
        }

        public static DeliveryTerm Of(IDeliveryTerm source) =>
            source == null ? DeliveryTerm.None : new DeliveryTerm(source.DeliveryTermKind, source.Incoterm, source.IncotermPlace, source.DeliveryTermText);

        public static DeliveryTerm Of(DeliveryTermDto dto) =>
            dto == null ? DeliveryTerm.None : new DeliveryTerm(dto.DeliveryTermKind, dto.Incoterm, dto.IncotermPlace, dto.DeliveryTermText);

        public static void Apply(IDeliveryTerm target, DeliveryTerm term)
        {
            target.DeliveryTermKind = term.Kind;
            target.Incoterm = term.Incoterm;
            target.IncotermPlace = term.Place;
            target.DeliveryTermText = term.Text;
        }

        // Vaste tekst voor de auditregel ("Incoterm FCA Venlo", "Franco: vanaf 10 pallets", leeg = null), invariant.
        public static string Describe(DeliveryTerm term) => term.Kind == null ? null
            : term.Kind == DeliveryTermKinds.Incoterm ? $"{term.Kind} {term.Incoterm} {term.Place}".Trim()
            : term.Text == null ? term.Kind : $"{term.Kind}: {term.Text}";

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
