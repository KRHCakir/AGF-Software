using System.Globalization;

namespace AgfAdmin.Services
{
    // Vaste Nederlandse sjablonen voor het mailconcept bij een document (documenten deel 3), met invulvelden. Per bedrijf aan te passen onder
    // Beheer (MailTemplates); zonder eigen sjabloon geldt dit standaardsjabloon. Geen AI: alleen invullen.
    public static class MailTemplateRules
    {
        public static readonly IReadOnlyList<string> Placeholders = new[] { "{bedrijf}", "{document}", "{nummer}", "{versie}", "{order}", "{partij}", "{klant}", "{laaddatum}", "{datum}" };

        public static (string Subject, string Body) Default(string kind) => kind switch
        {
            SalesOrderDocumentRules.KindTransportOrder => ("Transportopdracht {nummer} – {order}",
                "Beste,\n\nIn de bijlage transportopdracht {nummer} (versie {versie}) voor order {order} van {klant}.\nLaden op: {laaddatum}.\n\nWilt u bevestigen dat u de opdracht uitvoert?"),
            _ => ("Laadbon {nummer} – {order} – {klant}",
                "Beste,\n\nIn de bijlage laadbon {nummer} (versie {versie}) voor order {order} van {klant}, partij {partij}.\nTe laden op: {laaddatum}."),
        };

        // Onder elke tekst komt één afsluiting: de handtekening uit Beheer, of zonder handtekening deze (dus nooit twee keer een groet).
        public const string DefaultClosing = "Met vriendelijke groet,\n{bedrijf}";

        public static string DocumentName(string kind) => kind == SalesOrderDocumentRules.KindTransportOrder ? "Transportopdracht" : "Laadbon";

        // Vult de bekende invulvelden in; een onbekend veld blijft staan zoals het is.
        public static string Render(string template, IReadOnlyDictionary<string, string> values)
        {
            var text = template ?? "";
            foreach (var (key, value) in values) text = text.Replace(key, value ?? "", StringComparison.Ordinal);
            return text;
        }

        public static string Date(DateTime utc, bool withTime) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), AmsterdamBusinessCalendar.Zone).ToString(withTime ? "dd-MM-yyyy HH:mm" : "dd-MM-yyyy", CultureInfo.GetCultureInfo("nl-NL"));
    }
}
