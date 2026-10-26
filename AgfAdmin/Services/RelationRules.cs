using System.Text.RegularExpressions;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // Regels van de relatiewerkplek. Alleen de bedrijfsnaam is verplicht; een relatie bestaat ook met beperkte gegevens.
    // Zoekcodes zijn hoofdletterongevoelig uniek; namen zijn bewust niet uniek (gelijkende namen vragen om bevestiging, zoals bij
    // de snelle klantaanmaak). Formaatcontroles (btw, e-mail, GGN) bewijzen nooit geldigheid of certificering.
    public static class RelationRules
    {
        public const int MaxSearchCodeLength = 32;
        public const int MaxNameLength = 200;
        public const int MaxWebsiteLength = 200;
        public const int MaxEmailLength = 200;
        public const int MaxPhoneLength = 40;
        public const int MaxVatLength = 32;
        public const int MaxRegistrationLength = 64;
        public const int MaxRegistrationTypeLength = 32;
        public const int MaxLegalFormLength = 64;
        public const int MaxNoteLength = 1000;
        public const int MaxAddressLabelLength = 100;
        public const int MaxAddressLineLength = 300;
        public const int MaxPostalCodeLength = 20;
        public const int MaxCityLength = 100;
        public const int MaxContactNameLength = 100;
        public const int MaxDepartmentLength = 50;
        public const int MaxJobTitleLength = 100;
        public const int MaxContactNoteLength = 500;
        public const int MaxChannelLabelLength = 50;
        public const int MaxChannelValueLength = 200;
        public const int MaxProducerNameLength = 200;
        public const int MaxAddresses = 20;
        public const int MaxContacts = 50;
        public const int MaxChannelsPerContact = 10;
        public const int MaxGgnsPerVendor = 50;
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        public static readonly IReadOnlyList<string> Languages = new[] { "nl", "en", "de", "fr", "es", "it", "pl", "tr" };
        public static readonly IReadOnlyList<string> DepartmentSuggestions = new[] { "verkoop", "inkoop", "administratie", "transport", "kwaliteit", "directie" };
        public static readonly IReadOnlyList<string> RegistrationTypeSuggestions = new[] { "KvK", "HRB", "HRA", "SIRET", "CRN", "CIF", "NIP", "Other" };
        public static readonly IReadOnlyList<string> LegalFormSuggestions = new[] { "BV", "NV", "VOF", "Eenmanszaak", "Maatschap", "Stichting", "Coöperatie", "GmbH", "Ltd", "SARL", "SL", "Sp. z o.o.", "Other" };
        public static readonly IReadOnlyList<string> Currencies = new[] { "EUR", "USD", "GBP", "PLN", "TRY", "MAD", "ZAR" };

        // Officiële GLOBALG.A.P.-databank: certificaat zoeken gebeurt daar, nooit hier (geen scraping, geen status).
        public const string GlobalGapDatabaseUrl = "https://database.globalgap.org/";
        // Standaardlink naar de GLOBALG.A.P. Supply Chain Portal (bedrijfsinstelling, wijzigbaar via api/relations/settings).
        public const string DefaultGlobalGapPortalUrl = "https://prod.osapiens.cloud/portal/webbundle/foodplus/field-service-os/supply-chain-portal?app-route-hash=%252Fcertificates";
        public const int MaxPortalUrlLength = 500;

        private static readonly Regex SearchCode = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$", RegexOptions.Compiled);
        private static readonly Regex CountryCode = new("^[A-Z]{2}$", RegexOptions.Compiled);

        public static bool IsSearchCode(string value) => value != null && SearchCode.IsMatch(value);
        public static string SearchCodeKey(string searchCode) => string.IsNullOrWhiteSpace(searchCode) ? null : searchCode.Trim().ToLowerInvariant();

        public static string NormalizeCountry(string raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToUpperInvariant();
        public static bool IsCountryCode(string normalized) => normalized != null && CountryCode.IsMatch(normalized);

        // Absolute https-URL (bedrijfsinstelling met de portaallink).
        public static bool IsHttpsUrl(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host);

        public static bool IsLanguage(string value) => value != null && Languages.Contains(value, StringComparer.Ordinal);

        // Btw-nummer: alleen spaties, punten en streepjes vallen weg; verder alleen letters en cijfers (2-letterige landcode vooraan,
        // XI voor Noord-Ierland inbegrepen). Formaatcontrole, geen VIES-controle.
        public static string NormalizeVat(string raw) =>
            string.IsNullOrWhiteSpace(raw) ? null : new string(raw.Trim().Where(c => c != ' ' && c != '.' && c != '-').ToArray()).ToUpperInvariant();

        public static bool IsVatShape(string normalized) =>
            normalized != null && normalized.Length >= 4 && normalized.Length <= MaxVatLength
            && char.IsLetter(normalized[0]) && char.IsLetter(normalized[1]) && normalized.All(char.IsLetterOrDigit);

        // Bewust licht: één @ met tekst ervoor en erna en zonder spaties; geen volledige RFC-controle (zelfde regel als CustomerService).
        public static bool LooksLikeEmail(string value)
        {
            var at = value.IndexOf('@');
            return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && !value.Any(char.IsWhiteSpace);
        }

        public static bool IsAddressPurpose(string value) => value != null && AddressPurpose.All.Contains(value, StringComparer.Ordinal);
        public static bool IsChannelKind(string value) => value != null && ContactChannelKind.All.Contains(value, StringComparer.Ordinal);
    }
}
