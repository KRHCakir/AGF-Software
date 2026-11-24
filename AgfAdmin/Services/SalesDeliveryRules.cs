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
