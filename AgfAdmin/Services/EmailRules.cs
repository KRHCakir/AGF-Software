using System.Text.RegularExpressions;

namespace AgfAdmin.Services
{
    // Strikte e-mailvorm voor nieuwe of gewijzigde adressen (relatie-e-mail en e-mailkanalen van contacten). Bestaande, ongewijzigde
    // waarden worden niet opnieuw getoetst. Een bekende tikfout in het domein geeft een suggestie (waarschuwing), geen fout.
    // De frontend spiegelt StrictPattern en TypoDomains exact.
    //
    // Vorm: precies één @; lokaal deel = letters, cijfers en !#$%&'*+/=?^_`{|}~- in punt-gescheiden delen (geen punt vooraan,
    // achteraan of dubbel), max 64; domein = minstens twee labels gescheiden door punten, labels letters/cijfers/streepje (niet
    // vooraan/achteraan), laatste label (TLD) minstens 2 letters; geen spaties; totaal max 254.
    public static class EmailRules
    {
        public const string StrictPattern = "^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)+[A-Za-z]{2,63}$";
        public const int MaxLength = 254;
        public const int MaxLocalLength = 64;

        private static readonly Regex Strict = new(StrictPattern, RegexOptions.Compiled);

        // Tikfout in het domein -> bedoeld domein (kleine letters, exacte vergelijking op het hele domein).
        public static readonly IReadOnlyDictionary<string, string> TypoDomains = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gmial.com"] = "gmail.com", ["gmai.com"] = "gmail.com", ["gamil.com"] = "gmail.com", ["gmal.com"] = "gmail.com", ["gnail.com"] = "gmail.com",
            ["gmaill.com"] = "gmail.com", ["gmail.co"] = "gmail.com", ["gmail.con"] = "gmail.com", ["gmail.cm"] = "gmail.com", ["gmail.om"] = "gmail.com",
            ["hotmial.com"] = "hotmail.com", ["hotmai.com"] = "hotmail.com", ["hotmal.com"] = "hotmail.com", ["hotmil.com"] = "hotmail.com", ["homail.com"] = "hotmail.com",
            ["hotmail.co"] = "hotmail.com", ["hotmail.con"] = "hotmail.com", ["hotmial.nl"] = "hotmail.nl", ["hotmail.nll"] = "hotmail.nl",
            ["outlok.com"] = "outlook.com", ["outllok.com"] = "outlook.com", ["outloo.com"] = "outlook.com", ["outlook.co"] = "outlook.com", ["outlook.con"] = "outlook.com",
            ["outlok.nl"] = "outlook.nl",
            ["yahooo.com"] = "yahoo.com", ["yaho.com"] = "yahoo.com", ["yahoo.co"] = "yahoo.com", ["yahoo.con"] = "yahoo.com",
            ["iclod.com"] = "icloud.com", ["icloud.co"] = "icloud.com", ["icloud.con"] = "icloud.com",
            ["live.nll"] = "live.nl", ["live.co"] = "live.com",
            ["ziggo.nll"] = "ziggo.nl", ["zigo.nl"] = "ziggo.nl", ["ziggo.n"] = "ziggo.nl",
            ["kpnmial.nl"] = "kpnmail.nl", ["kpnmail.nll"] = "kpnmail.nl", ["kpnmal.nl"] = "kpnmail.nl",
            ["xs4al.nl"] = "xs4all.nl", ["xs4all.nll"] = "xs4all.nl",
            ["hetnet.nll"] = "hetnet.nl", ["planet.nll"] = "planet.nl", ["telfort.nll"] = "telfort.nl",
            ["t-onlin.de"] = "t-online.de", ["t-online.d"] = "t-online.de", ["gmx.d"] = "gmx.de", ["web.d"] = "web.de",
        };

        public static bool IsStrict(string value) =>
            value != null && value.Length <= MaxLength && Strict.IsMatch(value) && value.IndexOf('@') <= MaxLocalLength;

        public static string Domain(string value)
        {
            var at = value?.LastIndexOf('@') ?? -1;
            return at < 0 || at == value.Length - 1 ? null : value[(at + 1)..].ToLowerInvariant();
        }

        // Suggestie bij een bekende tikfout (zelfde lokaal deel, verbeterd domein), anders null.
        public static string TypoSuggestion(string value)
        {
            var domain = Domain(value);
            return domain != null && TypoDomains.TryGetValue(domain, out var fixedDomain) ? value[..value.LastIndexOf('@')] + "@" + fixedDomain : null;
        }

        public const string InvalidMessage = "Dit is geen geldig e-mailadres (één @, een domein met een punt, geen spaties of ongeldige tekens).";
    }
}
