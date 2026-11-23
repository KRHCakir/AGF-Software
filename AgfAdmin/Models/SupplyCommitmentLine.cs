namespace AgfAdmin.Models
{
    // Regel/partij van een toezegging. Alle nieuwe fysieke voorraad wordt via zo'n regel ontvangen,
    // dus elke voorraadbeweging en reservering is herleidbaar tot deze bron.
    public sealed class SupplyCommitmentLine
    {
        public int Id { get; set; }

        public int SupplyCommitmentId { get; set; }
        public SupplyCommitment SupplyCommitment { get; set; }

        public int ProductVariantId { get; set; }
        public ProductVariant ProductVariant { get; set; }

        public decimal ExpectedQuantity { get; set; }
        public string ExpectedUnit { get; set; }            // box, colli, tray, piece of dozen; optioneel tijdens de overgang
        public decimal? ExpectedNetWeightKg { get; set; }   // verwachte netto kg; bij WeightSource Entered werkelijk opgegeven, nooit afgeleid
        public decimal? ExpectedGrossWeightKg { get; set; } // verwachte bruto kg; nooit onder netto
        // Null zonder kg, Calculated = aantal × artikelstandaard, Entered = door de gebruiker opgegeven (LineWeightRules).
        // Regels van vóór deze kolom met netto kg zijn Entered (dat was toen de enige manier).
        public string WeightSource { get; set; }
        // Oorspronkelijke palletinvoer (optioneel, beide of geen): ExpectedQuantity = PalletCount × PackagesPerPallet + LoosePackages.
        // ExpectedQuantity blijft de bron voor alle controles; dit is alleen de herkenbare weergave "3 pallets × 120 + 15 colli".
        // Nieuwe invoer heeft hele pallets; oudere regels kunnen een gebroken palletaantal hebben (blijft leesbaar).
        public decimal? PalletCount { get; set; }
        public decimal? PackagesPerPallet { get; set; }
        // Losse verpakkingen naast de hele pallets; alleen bij palletinvoer, heel en >= 0.
        public decimal? LoosePackages { get; set; }
        public string LotNumber { get; set; } // optioneel

        // Certificaten deel 2: het GGN van het product (van de teler), zoals op de factuur van de leverancier. Voorgesteld uit de geldige
        // GGN-certificaten van de leverancier waarin het product staat (één treffer voorgevuld, meer = kiezen, geen = leeg); altijd te
        // overschrijven of leeg te maken, want een handelaar factureert het GGN van zijn teler, dat niet in zijn eigen certificaat staat.
        // 13 cijfers als tekst of null. Op een Open order met een Edit, op een definitieve order alleen met een correctie (regel 34).
        // Gaat bij ontvangst mee naar de pallet of partij (PalletContentLine.Ggn) en zo naar de laadbon en de transportopdracht.
        public string Ggn { get; set; }

        // Inkoopprijs per verpakking (de eenheid van de regel), excl. btw. Optioneel: een prijs kan ook pas bij afrekening komen.
        // Vast na aanmaken (TraceabilityGuard); het regeltotaal is aantal × prijs (PurchaseLineMath), nooit opgeslagen.
        public decimal? UnitPrice { get; set; }
        // Feedback 22 november: korting in procenten (null = geen) en het btw-tarief van de regel (voorstel uit het artikel, per regel aan te
        // passen; null bij regels van vóór deze ronde). Het bedrag is aantal × prijs min korting (OrderLineMath).
        public decimal? DiscountPercentage { get; set; }
        public decimal? VatPercentage { get; set; }

        // Partij van de regel: bij een nieuwe order die van de kop; bij oudere regels alleen als al hun ontvangsten eenduidig bij één
        // partij horen (migratie AddPartijen). Een vervangende regel na een afwijking erft de partij van de oorspronkelijke regel.
        public int? PartijId { get; set; }
        public Partij Partij { get; set; }

        // Vervangende regel na een bevestigde afwijkende ontvangst (SupplyReceiptDeviation): de oorspronkelijke regel. Onveranderlijk.
        public int? ReplacesLineId { get; set; }
        public SupplyCommitmentLine ReplacesLine { get; set; }

        // Feedback 22 november: splitsen. Verandert het artikel van een deels ontvangen regel, dan blijft het ontvangen deel op die regel en
        // wordt het open deel een nieuwe regel met het nieuwe artikel; die nieuwe regel wijst hier naar de oude. Alleen bij aanmaken gezet,
        // samen met een Edit-auditregel van de order (guard-regel 39); daarna onveranderlijk. Los van ReplacesLineId (afwijking, regel 18).
        public int? SplitFromLineId { get; set; }
        public SupplyCommitmentLine SplitFromLine { get; set; }
        // Vervallen restant: wat bij het splitsen niet meer verwacht wordt (null = niets vervallen). ExpectedQuantity is dan het ontvangen
        // aantal; besteld was ExpectedQuantity + CancelledQuantity. Telt niet in Verwacht of in de totalen; stijgt alleen met een Edit (regel 39).
        public decimal? CancelledQuantity { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token: wordt aangeraakt bij elke DirectTrade-toewijzing of vrijgave op deze regel,
        // zodat twee gelijktijdige toewijzingen nooit samen boven de verwachte hoeveelheid uitkomen. Centraal verhoogd in AppDbContext.
        public int Version { get; set; }
    }
}
