using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;

using AgfAdmin.Services;

namespace AgfAdmin.Models
{
    public class ProductVariant
    {
        public int Id { get; set; }
        
        public int ProductId { get; set; }
        [JsonIgnore]
        public Product Product { get; set; }

        public string Name { get; set; } // e.g. "Cherry"
        public string Origin { get; set; } // weergavenaam van de herkomst; met een land de Nederlandse landnaam ("Peru")
        // Herkomst als land uit de vaste lijst (Countries, ISO 3166-1 alpha-2). Null bij oude varianten waarvan de tekst niet
        // eenduidig een land was ("Herkomst opschonen" in Artikelen). Vergelijken en filteren gaat op deze code, nooit op de tekst.
        public string? OriginCountryCode { get; set; }
        public string? Season { get; set; } // bijv. "Mei - Augustus"
        public string? Notes { get; set; }  // bijv. "Let op: extra zoet"
        public string Packaging { get; set; } // e.g. "5kg Box"
        public string? SizeCategory { get; set; } // e.g. "Class I"

        [Column(TypeName = "decimal(18,2)")]
        public decimal DefaultPrice { get; set; }

        public string? ShortcutCode { get; set; }

        // Gewichtsmethode van deze variant/verpakking: "Weighed" (altijd werkelijk wegen) of "Calculated" (aantal ×
        // netto kg per verpakking volgens etiket). Bestaande varianten zijn Weighed. Een maat alleen bepaalt dit nooit.
        public string WeightMethod { get; set; } = WeightRules.Weighed;

        // Standaard netto kg per verpakking (etiket); alleen een invoerhulp, per inhoudsregel aanpasbaar.
        public decimal? DefaultNetKgPerPackage { get; set; }

        // Standaard verpakkingen per pallet; alleen een invoerhulp (vooraf ingevuld, aanpasbaar). Nooit een universele conversie.
        public decimal? DefaultPackagesPerPallet { get; set; }

        // Artikelwerkplek (ArticleService): aanvullende stamgegevens, alle optioneel. Standaarden voor nieuwe invoer; nooit een
        // garantie over een concrete partij en nooit met terugwerkende kracht op bestaande documenten of inhoudsregels.
        public string? Brand { get; set; }
        // Verpakkingseenheid voor nieuwe regels (box, colli, tray, piece, dozen); los van de vrije verpakkingsomschrijving.
        public string? PackagingUnit { get; set; }
        // Standaard bruto kg per verpakking (informatief; nooit een vervanging van netto gewicht in de voorraadberekening).
        public decimal? DefaultGrossKgPerPackage { get; set; }
        // Goederencode als tekst (voorloopnullen blijven), met expliciet codestelsel: HS (6), CN/GN (8) of TARIC (10).
        public string? GoodsCode { get; set; }
        public string? GoodsCodeSystem { get; set; }
        // Handelsklasse (EU-handelsnorm: Extra, I, II; vrije tekst). Geen keuringsstatus: "Klasse I" is niet "Goedgekeurd".
        public string? TradeClass { get; set; }
        // Oude standaard-GGN (vóór de verplaatsing naar leveranciers-GGN's in de relatiewerkplek). Niet meer in de artikelinvoer en
        // nergens meer voorgesteld; de kolom blijft bewaard en wordt gerapporteerd tot de waarden handmatig aan een leverancier zijn toegewezen.
        public string? DefaultGgn { get; set; }

        // Beschikbaarheidseenheid voor nieuwe voorraadflows: "Kilogram" of "Package".
        // Optioneel tijdens de overgang; nooit afgeleid uit maat of verpakking.
        public string AvailabilityUnit { get; set; }

        // Gearchiveerd (inactief): verdwijnt uit keuzelijsten en suggesties voor nieuwe invoer, blijft zichtbaar in de historie en kan
        // weer worden teruggezet. Voor een artikel waar al iets aan hangt (verwijderen kan dan niet). Nooit met terugwerkende kracht.
        public bool IsArchived { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public string? ArchivedBy { get; set; }

        // Idempotentiesleutel van "Artikel aanmaken en doorgaan" vanuit aanvoer of ontvangst: een herhaald verzoek maakt nooit een
        // tweede artikel. Leeg bij artikelen die op een andere manier zijn aangemaakt; uniek.
        [JsonIgnore]
        public Guid? RequestId { get; set; }

        // Relationships
        [JsonIgnore]
        public ICollection<OrderLine> OrderLines { get; set; }
        
        [JsonIgnore]
        public ICollection<Inventory> Inventories { get; set; }
    }
}
