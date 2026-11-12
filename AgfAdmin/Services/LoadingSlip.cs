using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // Documenten bij een verkooporder (documenten deel 1: de laadbon). Constanten en kleine regels zonder database.
    public static class SalesOrderDocumentRules
    {
        public const string KindLoadingSlip = "LoadingSlip";
        public const string ReasonConfirmed = "Confirmed";
        public const string ReasonChanged = "Changed";
        public const string LanguageEnglish = "en";
        public const string LanguageDutch = "nl";
        public static readonly IReadOnlyList<string> Languages = new[] { LanguageEnglish, LanguageDutch };
        public const int MaxLicensePlateLength = 32;
        public const int MaxWarehouseAddressLength = 300;
        public const int MaxActorLength = 100;

        // Het nummer van de laadbon: het VO-nummer met LB ervoor (VO-2026-0012 → LB-2026-0012), gelijk voor alle versies.
        public static string Number(string orderNumber) =>
            orderNumber != null && orderNumber.StartsWith("VO-", StringComparison.Ordinal) ? "LB-" + orderNumber[3..] : "LB-" + orderNumber;

        // Opnieuw doorgeven: er is een nieuwere versie dan de eerste en die is nog niet doorgegeven of verstuurd.
        public static bool NeedsResend(IReadOnlyCollection<SalesOrderDocument> versions)
        {
            var latest = versions.OrderByDescending(d => d.Version).FirstOrDefault();
            return latest != null && latest.Version > 1 && latest.SentAt == null;
        }
    }

    // De gegevens van één laadbon zoals ze op het moment van die versie waren: alleen wat de loods nodig heeft om te laden. Bewust geen
    // prijzen, bedragen of btw (ook niet van de emballage): die horen niet op een document voor de loods.
    public sealed record LoadingSlipSnapshot(
        string Language, bool ShowCustomer, bool ShowSupplier,
        string CompanyName, string CompanyAddress, string LogoImage,
        string WarehouseAddress,
        string CustomerName, string Position, string OrderNumber, string CustomerReference,
        DateTime RequestedDeliveryDate, DateTime? LoadAt, string LicensePlate,
        List<LoadingSlipRow> Rows, List<LoadingSlipPackagingRow> Packaging,
        decimal TotalQuantity, decimal? TotalPallets, decimal? TotalNetKg,
        DateTime ConfirmedAt, string ConfirmedBy)
    {
        // Alleen om bestaande snapshots te kunnen lezen: oudere versies bewaarden het pad van het logo (LogoPath) in plaats van de inhoud.
        // Nieuwe snapshots laten dit veld leeg (en dus weg uit de JSON, zodat de vingerafdruk gelijk blijft).
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string LogoPath { get; init; }
    }

    // Eén rij: een orderregel, en per pallet of partij waar dat al vastligt (gereserveerd); wat nog niet gereserveerd is een eigen rij.
    // Pallets niet afgerond (de bon toont twee decimalen), zelfde berekening als op de orderregel (PurchaseLineMath).
    public sealed record LoadingSlipRow(
        decimal Quantity, string Unit, decimal? Pallets, string Description, string Variety, string Packaging, string Brand, string Size,
        string Origin, decimal? NetKg, string Supplier, string PalletReference);

    // Emballage die mee gaat (Pallet wegwerp, Euro-pallet, Krat): alleen het aantal, nooit een prijs.
    public sealed record LoadingSlipPackagingRow(int Quantity, string Description);

    public static class LoadingSlipBuilder
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        // Voor de vingerafdruk zonder lege velden: komt er later een veld bij de bon, dan geeft een bestaande bon zonder dat veld dezelfde
        // vingerafdruk en dus geen onterechte nieuwe versie. Niet meer wijzigen zodra er laadbonnen bewaard zijn.
        private static readonly JsonSerializerOptions HashJson = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        // De order moet geladen zijn met klant, verkoop-partij, regels (artikel, product, reserveringen met pallet, partij en de leverancier
        // van de inkooporder) en emballage (kostenartikel).
        public static LoadingSlipSnapshot Build(SalesOrder order, CompanySettings settings)
        {
            var rows = new List<LoadingSlipRow>();
            foreach (var line in order.Lines.OrderBy(l => l.Id))
            {
                var variant = line.ProductVariant;
                var perPallet = PurchaseLineMath.PackagesPerPallet(line.PackagesPerPallet, variant?.DefaultPackagesPerPallet);
                LoadingSlipRow Row(decimal quantity, decimal? netKg, string supplier, string pallet) => new(
                    quantity, line.Unit, PurchaseLineMath.Pallets(quantity, perPallet), variant?.Product?.Name, variant?.Name, Blank(variant?.Packaging), Blank(variant?.Brand),
                    Blank(variant?.SizeCategory), Blank(variant?.OriginCountryCode) ?? Blank(variant?.Origin), netKg, settings.LoadingSlipShowSupplier ? supplier : null, pallet);
                decimal? Share(decimal quantity) => line.NetWeightKg is decimal kg && line.Quantity > 0m ? Math.Round(kg * quantity / line.Quantity, 3, MidpointRounding.AwayFromZero) : null;

                // Gereserveerd of al verzonden: per pallet (fysiek) of per orderregel van de inkoop (direct trade); vrijgegeven telt niet.
                var reserved = line.Reservations.Where(r => r.Status is "Active" or "Consumed").ToList();
                foreach (var group in reserved.GroupBy(r => (r.PalletContentLineId, r.SupplyCommitmentLineId)).OrderBy(g => g.Min(r => r.Id)))
                {
                    var first = group.First();
                    var content = first.PalletContentLine;
                    // Bij een fysieke reservering komt de herkomst ook via de palletinhoud (zelfde bronregel als de reservering, guardregel 1).
                    var supplyLine = first.SupplyCommitmentLine ?? content?.SupplyCommitmentLine;
                    var quantity = group.Sum(r => r.Quantity);
                    var kg = group.All(r => r.NetWeightKg != null) ? group.Sum(r => r.NetWeightKg.Value) : Share(quantity);
                    var lot = content?.Partij?.Number ?? Blank(content?.LotNumber) ?? supplyLine?.Partij?.Number ?? supplyLine?.SupplyCommitment?.Partij?.Number;
                    var vendor = supplyLine?.SupplyCommitment?.Vendor?.Name;
                    rows.Add(Row(quantity, kg, Join(vendor, lot), content?.Pallet?.Reference));
                }
                var open = line.Quantity - reserved.Sum(r => r.Quantity);
                if (open > 0m) rows.Add(Row(open, Share(open), null, null));
            }
            var packaging = order.PackagingLines.OrderBy(p => p.Id).Select(p => new LoadingSlipPackagingRow(p.Quantity, p.CostArticle?.Name)).ToList();
            var pallets = rows.Where(r => r.Pallets != null).Select(r => r.Pallets.Value).ToList();
            var kgs = rows.Where(r => r.NetKg != null).Select(r => r.NetKg.Value).ToList();
            return new LoadingSlipSnapshot(
                SalesOrderDocumentRules.Languages.Contains(settings.LoadingSlipLanguage) ? settings.LoadingSlipLanguage : SalesOrderDocumentRules.LanguageEnglish,
                settings.LoadingSlipShowCustomer, settings.LoadingSlipShowSupplier,
                settings.CompanyName, Blank(settings.Address), LogoImageOf(settings.LogoPath),
                Blank(settings.LoadingSlipWarehouseAddress),
                settings.LoadingSlipShowCustomer ? order.Customer?.Name : null, order.SalesPartij?.Number, order.OrderNumber, Blank(order.CustomerReference),
                Second(order.RequestedDeliveryDate), order.LoadAt == null ? null : Second(order.LoadAt.Value), Blank(order.LicensePlate),
                rows, packaging, rows.Sum(r => r.Quantity), pallets.Count == 0 ? null : pallets.Sum(), kgs.Count == 0 ? null : kgs.Sum(),
                Second(order.ConfirmedAt ?? DateTime.UtcNow), order.ConfirmedBy);
        }

        public static string Serialize(LoadingSlipSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);
        public static LoadingSlipSnapshot Deserialize(string json) => JsonSerializer.Deserialize<LoadingSlipSnapshot>(json, Json);

        // Vingerafdruk van de inhoud: dezelfde bon (ook na opnieuw opbouwen) geeft nooit een nieuwe versie.
        public static string Hash(LoadingSlipSnapshot snapshot) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, HashJson)))).ToLowerInvariant();

        // Op de seconde en als UTC: een datum uit de database (zonder Kind, met minder precisie) geeft zo dezelfde bon en dus geen nieuwe versie.
        private static DateTime Second(DateTime value) =>
            DateTime.SpecifyKind(new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

        // De inhoud van het logo (base64) gaat mee in de snapshot, zodat een oude versie niet verandert als het bestand wordt vervangen of verdwijnt.
        private static string LogoImageOf(string path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string Join(string vendor, string lot) =>
            (Blank(vendor), Blank(lot)) switch { (null, null) => null, (var v, null) => v, (null, var l) => l, (var v, var l) => $"{v} · {l}" };
    }
}
