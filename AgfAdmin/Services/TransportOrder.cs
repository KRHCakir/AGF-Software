using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // De transportopdracht (documenten deel 2) voor de vervoerder: wat er waar geladen en gelost wordt. Een snapshot per versie, zoals de
    // laadbon; de PDF komt altijd uit die snapshot. Nooit een prijs, bedrag, tarief of btw: alleen aantallen, pallets en kilo's.
    public sealed record TransportOrderSnapshot(
        string Language, string CompanyName, string CompanyAddress, string LogoImage,
        bool OwnTransport, string CarrierName, string CarrierAddress,
        string OrderNumber, string CustomerName, string LoadingPlace, string UnloadingPlace,
        DateTime RequestedDeliveryDate, DateTime? LoadAt, DateTime? UnloadDate, decimal? TemperatureC,
        string LoadingReference, string UnloadingReference, string Remarks, string PalletKind,
        List<TransportOrderRow> Rows, List<LoadingSlipPackagingRow> Packaging,
        decimal TotalQuantity, decimal? TotalPalletSpaces, decimal? TotalGrossKg,
        DateTime ConfirmedAt, string ConfirmedBy);

    // Eén rij per orderregel: colli, omschrijving (product, ras, verpakking), klasse, herkomst, palletplaatsen (niet afgerond, zelfde
    // berekening als op de orderregel) en bruto kg (van de regel, berekend of aangepast; anders uit het artikel).
    public sealed record TransportOrderRow(decimal Quantity, string Unit, string Description, string Class, string Origin, decimal? PalletSpaces, decimal? GrossKg);

    public static class TransportOrderBuilder
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        // De order moet geladen zijn met klant (relatie met adressen), regels (artikel, product), emballage (kostenartikel) en het transport
        // (vervoerder met adressen, gekozen losadres). Zonder transport is er geen opdracht.
        public static TransportOrderSnapshot Build(SalesOrder order, CompanySettings settings)
        {
            var transport = order.Transport ?? throw new InvalidOperationException($"Sales order {order.OrderNumber} has no transport.");
            var rows = order.Lines.OrderBy(l => l.Id).Select(line =>
            {
                var variant = line.ProductVariant;
                var perPallet = PurchaseLineMath.PackagesPerPallet(line.PackagesPerPallet, variant?.DefaultPackagesPerPallet);
                var description = string.Join(" · ", new[] { variant?.Product?.Name, variant?.Name, LoadingSlipBuilder.Blank(variant?.Packaging) }.Where(p => !string.IsNullOrWhiteSpace(p)));
                return new TransportOrderRow(line.Quantity, line.Unit, description, LoadingSlipBuilder.Blank(variant?.TradeClass),
                    LoadingSlipBuilder.Blank(variant?.OriginCountryCode) ?? LoadingSlipBuilder.Blank(variant?.Origin), PurchaseLineMath.Pallets(line.Quantity, perPallet), GrossKg(line));
            }).ToList();
            var packaging = order.PackagingLines.OrderBy(p => p.Id).Select(p => new LoadingSlipPackagingRow(p.Quantity, p.CostArticle?.Name)).ToList();
            // Het totaal van de regels met een waarde, zoals op de laadbon. Heeft een regel geen waarde (geen colli per pallet of geen bruto),
            // dan blijft dat in de rijen zichtbaar en zegt de PDF bij het totaal dat het alleen de regels met een waarde telt.
            var spaces = rows.Where(r => r.PalletSpaces != null).Select(r => r.PalletSpaces.Value).ToList();
            var gross = rows.Where(r => r.GrossKg != null).Select(r => r.GrossKg.Value).ToList();
            var own = transport.Mode == SalesTransportRules.ModeOwn;
            return new TransportOrderSnapshot(
                SalesOrderDocumentRules.Languages.Contains(settings.TransportOrderLanguage) ? settings.TransportOrderLanguage : SalesOrderDocumentRules.LanguageEnglish,
                settings.CompanyName, LoadingSlipBuilder.Blank(settings.Address), LoadingSlipBuilder.LogoImageOf(settings.LogoPath),
                own, own ? null : transport.CarrierRelation?.Name, own ? null : AddressText(MainAddress(transport.CarrierRelation)),
                order.OrderNumber, order.Customer?.Name, LoadingPlace(transport, settings), UnloadingPlace(order, transport),
                LoadingSlipBuilder.Second(order.RequestedDeliveryDate), order.LoadAt == null ? null : LoadingSlipBuilder.Second(order.LoadAt.Value),
                transport.UnloadDate == null ? null : DateTime.SpecifyKind(transport.UnloadDate.Value.Date, DateTimeKind.Utc), transport.TemperatureC,
                LoadingSlipBuilder.Blank(transport.LoadingReference), LoadingSlipBuilder.Blank(transport.UnloadingReference) ?? LoadingSlipBuilder.Blank(order.CustomerReference),
                LoadingSlipBuilder.Blank(transport.Remarks), LoadingSlipBuilder.Blank(transport.PalletKind) ?? LoadingSlipBuilder.Blank(settings.TransportOrderPalletKind) ?? SalesTransportRules.DefaultPalletKind,
                rows, packaging, rows.Sum(r => r.Quantity), spaces.Count == 0 ? null : spaces.Sum(), gross.Count == 0 ? null : gross.Sum(),
                LoadingSlipBuilder.Second(order.ConfirmedAt ?? DateTime.UtcNow), order.ConfirmedBy);
        }

        public static string Serialize(TransportOrderSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);
        public static TransportOrderSnapshot Deserialize(string json) => JsonSerializer.Deserialize<TransportOrderSnapshot>(json, Json);

        // Vingerafdruk zoals bij de laadbon: lege velden tellen niet mee.
        public static string Hash(TransportOrderSnapshot snapshot) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, LoadingSlipBuilder.HashJson)))).ToLowerInvariant();

        // Bruto van de regel (berekend of door de gebruiker aangepast); zonder dat aantal × het standaard bruto per verpakking van het artikel.
        public static decimal? GrossKg(SalesOrderLine line) =>
            line.GrossWeightKg ?? (line.ProductVariant?.DefaultGrossKgPerPackage is decimal perPackage ? Math.Round(perPackage * line.Quantity, 3, MidpointRounding.AwayFromZero) : null);

        // De laadplaats: per order overschreven, anders de loods uit de instellingen, anders bedrijfsnaam en -adres.
        public static string LoadingPlace(SalesOrderTransport transport, CompanySettings settings) =>
            LoadingSlipBuilder.Blank(transport?.LoadingPlace) ?? LoadingSlipBuilder.Blank(settings.LoadingSlipWarehouseAddress)
            ?? string.Join(", ", new[] { settings.CompanyName, settings.Address }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

        // De losplaats: een afwijkend adres gaat voor; anders het gekozen adres van de klant, anders haar standaard afleveradres, anders haar
        // hoofdadres. Altijd met de naam van de klant ervoor.
        public static string UnloadingPlace(SalesOrder order, SalesOrderTransport transport)
        {
            var free = LoadingSlipBuilder.Blank(transport?.UnloadingPlace);
            if (free != null) return free;
            var address = transport?.UnloadingAddress ?? DeliveryAddress(order.Customer?.Relation);
            var text = AddressText(address);
            return string.Join(", ", new[] { order.Customer?.Name, text }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        public static RelationAddress DeliveryAddress(Relation relation) =>
            relation?.Addresses.Where(a => a.IsActive && a.Purpose == AddressPurpose.Delivery && a.IsDefault).OrderBy(a => a.SortOrder).ThenBy(a => a.Id).FirstOrDefault()
            ?? MainAddress(relation);

        public static RelationAddress MainAddress(Relation relation) =>
            relation?.Addresses.Where(a => a.IsActive).OrderByDescending(a => a.Purpose == AddressPurpose.Main).ThenByDescending(a => a.IsDefault).ThenBy(a => a.SortOrder).ThenBy(a => a.Id).FirstOrDefault();

        // Eén regel: label, straat, postcode en plaats, land. Leeg als er geen adres is.
        public static string AddressText(RelationAddress address)
        {
            if (address == null) return null;
            var city = string.Join(" ", new[] { address.PostalCode, address.City }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
            var parts = new[] { address.Label, address.Line1, address.Line2, city, address.CountryCode }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
    }
}
