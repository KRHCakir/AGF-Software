using System.Text;
using System.Text.RegularExpressions;

namespace AgfAdmin.Services
{
    // Vaste waarden en rekenregels van de partij. Gespiegeld in agf-frontend/src/lib/partijRules.ts; beide kanten hebben tests op
    // dezelfde gevallen (sleutel, formaat, voorbeeld).
    public static class PartijRules
    {
        public const int MaxNumberLength = 100;
        public const int MaxNoteLength = 500;
        public const int MaxReasonLength = 500;
        public const int MaxSupplierLotReferenceLength = 100;
        public const int MaxFormatLength = 40;
        public const int MaxActorLength = 100;
        // Aantal cijfers van het volgnummer (bedrijfsinstelling, standaard 3: 52611002 is 5, jaar 26, maand 11, nummer 002); een groter
        // volgnummer groeit gewoon door. Een eigen of oud nummer met een ander aantal cijfers (5261145) blijft geldig.
        public const int DefaultSequenceDigits = 3;
        public const int MinSequenceDigits = 1;
        public const int MaxSequenceDigits = 9;

        // Soort partij. Inkoop: één leverancier, zoals in P1 (inkooporder, ontvangst, palletregels). Verkoop: precies één verkooporder voor
        // één klant; de colli komen via de reserveringen op palletregels uit een of meer inkoop-partijen (geen eigen verdeeltabel). Elke
        // soort heeft een eigen formaat en teller; het nummer is uniek per soort.
        public const string KindPurchase = "Purchase";
        public const string KindSales = "Sales";
        public static readonly IReadOnlyList<string> Kinds = new[] { KindPurchase, KindSales };

        // Opgeslagen status. Uitverkocht is afgeleid (DisplayStatus); Afgesloten (resultaat definitief) komt in P3.
        public const string StatusOpen = "Open";
        public const string StatusSoldOut = "SoldOut";

        public const string ModeAuto = "Auto";
        public const string ModeOwn = "Own";
        public static readonly IReadOnlyList<string> Modes = new[] { ModeAuto, ModeOwn };

        public const string ResetNever = "Never";
        public const string ResetYearly = "Yearly";
        public const string ResetMonthly = "Monthly";
        public static readonly IReadOnlyList<string> Resets = new[] { ResetNever, ResetYearly, ResetMonthly };

        public const string DefaultFormat = "{JJ}{MM}{volgnr}";
        // Verkoop-partijen: zonder vaste 5 vooraan (261140 = jaar 26, maand 11, nummer 140).
        public const string DefaultSalesFormat = "{JJ}{MM}{volgnr}";
        public const string SequencePlaceholder = "{volgnr}";
        public static readonly IReadOnlyList<string> Placeholders = new[] { "{JJ}", "{JJJJ}", "{MM}", SequencePlaceholder };

        // Historie (PartijAuditEvent.EventType). Het aanmaken staat op de partij zelf (CreatedAt, CreatedBy).
        public const string EventNumberCorrected = "NumberCorrected";
        public const string EventOrderLinked = "OrderLinked";
        public const string EventDeleted = "Deleted";
        // Alleen bij een verkoop-partij zolang er op haar order niets gereserveerd of geleverd is: nummer aangepast zonder reden
        // (regel 24). Daarna gaat het nummer, net als bij een inkoop-partij, alleen via NumberCorrected met reden.
        public const string EventNumberChanged = "NumberChanged";

        // Partijen uit bestaande palletregels (migratie AddPartijen) hebben geen ingelogde aanmaker.
        public const string MigrationActor = "Migratie";

        private static readonly Regex PlaceholderPattern = new(@"\{[^{}]*\}", RegexOptions.CultureInvariant);

        // Vergelijkingssleutel: hoofdletters, zonder spaties (ook tabs) en streepjes. 2610251, "2610 251" en "2610-251" zijn één partij.
        // Null als er niets overblijft. Dezelfde regel staat als SQL in de migratie AddPartijen (alleen spatie en streepje: tabs komen in
        // de bestaande, getrimde invoer niet voor).
        public static string NumberKey(string number)
        {
            if (number == null)
            {
                return null;
            }
            var key = new StringBuilder(number.Length);
            foreach (var c in number)
            {
                if (!char.IsWhiteSpace(c) && c != '-')
                {
                    key.Append(char.ToUpperInvariant(c));
                }
            }
            return key.Length == 0 ? null : key.ToString();
        }

        public static bool IsMode(string value) => value != null && Modes.Contains(value, StringComparer.Ordinal);
        public static bool IsKind(string value) => value != null && Kinds.Contains(value, StringComparer.Ordinal);
        public static bool IsSequenceDigits(int value) => value >= MinSequenceDigits && value <= MaxSequenceDigits;
        public static bool IsReset(string value) => value != null && Resets.Contains(value, StringComparer.Ordinal);

        // Null als het formaat geldig is, anders de melding: verplicht {volgnr} (precies één keer), alleen bekende plaatshouders en
        // geen losse accolades. Vaste tekst eromheen mag: 5{JJ}{MM}{volgnr} geeft 52611002.
        public static string FormatError(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return "The format is required, for example {JJ}{MM}{volgnr}.";
            }
            if (format.Trim().Length > MaxFormatLength)
            {
                return $"The format may contain at most {MaxFormatLength} characters.";
            }
            var unknown = PlaceholderPattern.Matches(format).Select(m => m.Value).FirstOrDefault(p => !Placeholders.Contains(p, StringComparer.Ordinal));
            if (unknown != null)
            {
                return $"Unknown placeholder {unknown}; use {string.Join(", ", Placeholders)}.";
            }
            var rest = PlaceholderPattern.Replace(format, "");
            if (rest.Contains('{') || rest.Contains('}'))
            {
                return "A brace that does not belong to a placeholder; use {JJ}, {JJJJ}, {MM} and {volgnr}.";
            }
            var sequenceCount = PlaceholderPattern.Matches(format).Count(m => m.Value == SequencePlaceholder);
            if (sequenceCount != 1)
            {
                return "The format needs {volgnr} exactly once, otherwise two lots would get the same number.";
            }
            return null;
        }

        // Nummer volgens het formaat voor deze datum en dit volgnummer: {JJ}{MM}{volgnr} op 27-10-2026 met 251 = 2610251, en
        // 5{JJ}{MM}{volgnr} op 05-11-2026 met 2 = 52611002 (volgnummer aangevuld tot het ingestelde aantal cijfers).
        public static string Format(string format, DateOnly date, int sequence, int digits = DefaultSequenceDigits) =>
            PlaceholderPattern.Replace(format.Trim(), match => match.Value switch
            {
                "{JJ}" => (date.Year % 100).ToString("D2"),
                "{JJJJ}" => date.Year.ToString("D4"),
                "{MM}" => date.Month.ToString("D2"),
                SequencePlaceholder => sequence.ToString($"D{Math.Clamp(digits, MinSequenceDigits, MaxSequenceDigits)}"),
                _ => match.Value
            });

        // Periode waarin het volgnummer telt: "" (nooit opnieuw), "2026" (per jaar) of "2026-11" (per maand).
        public static string SequencePeriod(string reset, DateOnly date) => reset switch
        {
            ResetNever => "",
            ResetMonthly => $"{date.Year:D4}-{date.Month:D2}",
            _ => date.Year.ToString("D4")
        };

        // Weergavestatus: Uitverkocht als er ooit iets binnenkwam en er niets meer op voorraad (goedgekeurd of in keuring) of nog
        // verwacht is. Een partij zonder ontvangst blijft Open. De opgeslagen status gaat voor zodra die meer is dan Open (P3).
        public static string DisplayStatus(string storedStatus, bool anyReceived, decimal onHandQuantity, decimal openExpectedQuantity) =>
            storedStatus != StatusOpen ? storedStatus
            : anyReceived && onHandQuantity <= 0m && openExpectedQuantity <= 0m ? StatusSoldOut
            : StatusOpen;
    }
}
