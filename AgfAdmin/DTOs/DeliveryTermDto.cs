using AgfAdmin.Models;

namespace AgfAdmin.DTOs
{
    // Leveringsconditie van een order (feedback 22 november). In een aanvraag: weggelaten (null) = die van de klant of leverancier
    // overnemen (aanmaken) of ongewijzigd laten (aanpassen); meegestuurd met een lege soort = geen conditie.
    public sealed class DeliveryTermDto
    {
        public string DeliveryTermKind { get; set; }
        public string Incoterm { get; set; }
        public string IncotermPlace { get; set; }
        public string DeliveryTermText { get; set; }

        public static DeliveryTermDto From(IDeliveryTerm source) => source == null ? null : new DeliveryTermDto
        {
            DeliveryTermKind = source.DeliveryTermKind, Incoterm = source.Incoterm, IncotermPlace = source.IncotermPlace, DeliveryTermText = source.DeliveryTermText,
        };
    }
}
