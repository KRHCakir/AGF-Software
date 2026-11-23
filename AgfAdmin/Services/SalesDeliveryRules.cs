using AgfAdmin.DTOs;
using AgfAdmin.Models;

namespace AgfAdmin.Services
{
    // Leveringen van een verkooporder (feedback 22 november): een verkoop-partij is één levering aan één klant. Regels zonder database.
    public static class SalesDeliveryRules
    {
        public const string DeliveryCreatedEvent = "SalesDeliveryCreated";
        public const int MaxReservationsPerDelivery = 200;

        // Hoort deze reservering bij deze levering? Een reservering zonder levering hoort bij de eerste.
        public static bool Belongs(StockReservation reservation, SalesDelivery delivery) =>
            reservation.SalesDeliveryId == delivery.Id || (reservation.SalesDeliveryId == null && delivery.Sequence == 1);

        public static List<SalesDelivery> Ordered(SalesOrder order) => order.Deliveries.OrderBy(d => d.Sequence).ToList();

        // Het nummer van een document van een levering: de eerste levering houdt het nummer van vóór de leveringen (LB-2026-0012), een
        // deellevering krijgt haar volgnummer erachter (LB-2026-0012-2).
        public static string DocumentNumber(string kind, string orderNumber, int sequence) =>
            sequence <= 1 ? SalesOrderDocumentRules.NumberFor(kind, orderNumber) : $"{SalesOrderDocumentRules.NumberFor(kind, orderNumber)}-{sequence}";

        // De order zoals één levering haar ziet, voor de laadbon en de transportopdracht (dezelfde bouwers als vóór de leveringen):
        // - per regel alleen de reserveringen van deze levering, en wat nog niet gereserveerd is alleen op de laatste levering;
        // - de regel telt het aantal van deze levering (kg naar verhouding); een regel zonder iets op deze levering valt weg;
        // - emballage gaat mee met de eerste levering; laden op en kenteken zijn die van de levering (de eerste: die van de order);
        // - de verkoop-partij (positie op de bon) is die van de levering.
        // Een order met één levering ziet er precies zo uit als de order zelf: dezelfde bon en dezelfde vingerafdruk, dus geen nieuwe versie.
        // Het resultaat wordt nooit opgeslagen (het is geen gevolgde entiteit).
        public static SalesOrder View(SalesOrder order, SalesDelivery delivery)
        {
            var deliveries = Ordered(order);
            var last = deliveries.Count == 0 || deliveries[^1].Id == delivery.Id;
            var lines = new List<SalesOrderLine>();
            foreach (var line in order.Lines.OrderBy(l => l.Id))
            {
                var live = line.Reservations.Where(r => r.Status is ReservationRules.StatusActive or ReservationRules.StatusConsumed).ToList();
                var mine = live.Where(r => Belongs(r, delivery)).ToList();
                var reservedHere = mine.Sum(r => r.Quantity);
                var open = Math.Max(0m, line.Quantity - live.Sum(r => r.Quantity));
                var quantity = reservedHere + (last ? open : 0m);
                if (quantity <= 0m) continue;
                lines.Add(quantity == line.Quantity && mine.Count == live.Count ? line : new SalesOrderLine
                {
                    Id = line.Id, SalesOrderId = line.SalesOrderId, ProductVariantId = line.ProductVariantId, ProductVariant = line.ProductVariant,
                    Quantity = quantity, Unit = line.Unit, SupplySource = line.SupplySource, PalletCount = null, PackagesPerPallet = line.PackagesPerPallet,
                    NetWeightKg = Scale(line.NetWeightKg, quantity, line.Quantity), GrossWeightKg = Scale(line.GrossWeightKg, quantity, line.Quantity),
                    WeightSource = line.WeightSource, CreatedAt = line.CreatedAt, Version = line.Version,
                    Reservations = line.Reservations.Where(r => Belongs(r, delivery)).ToList(),
                });
            }
            var first = delivery.Sequence <= 1;
            return new SalesOrder
            {
                Id = order.Id, RequestId = order.RequestId, CustomerId = order.CustomerId, Customer = order.Customer, Status = order.Status,
                OrderNumber = order.OrderNumber, OrderYear = order.OrderYear, OrderSequence = order.OrderSequence,
                CustomerReference = order.CustomerReference, RequestedDeliveryDate = order.RequestedDeliveryDate, ActorReference = order.ActorReference,
                DeliveryTermKind = order.DeliveryTermKind, Incoterm = order.Incoterm, IncotermPlace = order.IncotermPlace, DeliveryTermText = order.DeliveryTermText,
                ConfirmedAt = order.ConfirmedAt, ConfirmedBy = order.ConfirmedBy,
                LoadAt = first ? order.LoadAt : delivery.LoadAt, LicensePlate = first ? order.LicensePlate : delivery.LicensePlate,
                CreatedAt = order.CreatedAt, UpdatedAt = order.UpdatedAt, Version = order.Version,
                Lines = lines,
                PackagingLines = first ? order.PackagingLines : new List<SalesOrderPackagingLine>(),
                ServiceLines = order.ServiceLines,
                Transport = order.Transport,
                Deliveries = new List<SalesDelivery> { delivery },
            };
        }

        private static decimal? Scale(decimal? value, decimal part, decimal whole) =>
            value is decimal v && whole > 0m ? (part == whole ? v : Math.Round(v * part / whole, 3, MidpointRounding.AwayFromZero)) : null;

        // Laden op en kenteken van een levering: de eerste gebruikt die van de order.
        public static (DateTime? LoadAt, string LicensePlate) Loading(SalesOrder order, SalesDelivery delivery) =>
            delivery.Sequence <= 1 ? (order.LoadAt, order.LicensePlate) : (delivery.LoadAt, delivery.LicensePlate);

        public static List<SalesDeliveryDto> ToDtos(SalesOrder order)
        {
            var deliveries = Ordered(order);
            var reservations = order.Lines.SelectMany(l => l.Reservations).ToList();
            return deliveries.Select((d, index) =>
            {
                var (loadAt, plate) = Loading(order, d);
                return new SalesDeliveryDto
                {
                    Id = d.Id, Sequence = d.Sequence, PartijId = d.PartijId, PartijNumber = d.Partij?.Number, Last = index == deliveries.Count - 1,
                    LoadAt = loadAt == null ? null : DateTime.SpecifyKind(loadAt.Value, DateTimeKind.Utc), LicensePlate = plate,
                    ReservationIds = reservations.Where(r => Belongs(r, d)).Select(r => r.Id).OrderBy(id => id).ToList(),
                    CreatedAt = DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc), CreatedBy = d.CreatedBy,
                };
            }).ToList();
        }
    }
}
