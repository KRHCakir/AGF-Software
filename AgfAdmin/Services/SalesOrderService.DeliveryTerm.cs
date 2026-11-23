using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Feedback 22 november: de leveringsconditie per verkooporder aanpassen (de klant verandert niet). Alleen een open order, met de
    // verwachte orderversie; elke wijziging is een auditregel DeliveryTermChanged met oud en nieuw (guard-regel 37). De laadbon en de
    // transportopdracht tonen de conditie niet, dus hun versies veranderen hierdoor niet.
    public sealed partial class SalesOrderService
    {
        public async Task<ServiceResult<SalesOrderDto>> UpdateDeliveryTermAsync(int salesOrderId, UpdateSalesOrderDeliveryTermDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            if (request.RequestId == null || request.RequestId == Guid.Empty)
            {
                ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
            if (request.ExpectedOrderVersion == null)
            {
                ValidationErrors.Add(errors, "expectedOrderVersion", "The order version the change is based on is required.");
            }
            // Weggelaten (null) = ongewijzigd; wissen kan alleen met een meegestuurd object met een lege soort.
            var term = request.DeliveryTerm == null ? null : DeliveryTermRules.Normalize(DeliveryTermRules.Of(request.DeliveryTerm), "deliveryTerm", errors);
            if (errors.Count > 0)
            {
                return ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            if (term == null)
            {
                return await GetAsync(salesOrderId, cancellationToken);
            }

            // Dezelfde opdracht nog eens (zelfde requestId): de order zoals ze nu is, mits het dezelfde wijziging is.
            var previous = await _db.SalesOrderAuditEvents.AsNoTracking()
                .Where(e => e.RequestId == request.RequestId && e.SalesOrderId == salesOrderId)
                .Select(e => new { e.EventType, e.NewValue }).FirstOrDefaultAsync(cancellationToken);
            if (previous != null)
            {
                return IsSameTermChange(previous.EventType, previous.NewValue, term)
                    ? await GetAsync(salesOrderId, cancellationToken)
                    : ServiceResult<SalesOrderDto>.Conflict($"RequestId {request.RequestId} was already used for a different change.");
            }

            var order = await _db.SalesOrders.SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken);
            if (order == null)
            {
                return ServiceResult<SalesOrderDto>.NotFound($"Sales order {salesOrderId} does not exist.");
            }
            if (order.Status != ReservationRules.OrderStatusOpen)
            {
                return ServiceResult<SalesOrderDto>.Conflict($"Sales order {salesOrderId} is {order.Status}; its delivery term no longer changes.");
            }
            if (order.Version != request.ExpectedOrderVersion)
            {
                return ServiceResult<SalesOrderDto>.Conflict($"Sales order {salesOrderId} is at version {order.Version}, not {request.ExpectedOrderVersion}; reload it and decide again.");
            }
            var before = DeliveryTermRules.Of(order);
            if (before == term)
            {
                return await GetAsync(salesOrderId, cancellationToken);
            }

            var now = DateTime.UtcNow;
            DeliveryTermRules.Apply(order, term);
            order.UpdatedAt = now;
            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrderId = order.Id,
                EventType = ReservationRules.DeliveryTermChangedEvent,
                OldValue = DeliveryTermRules.Describe(before),
                NewValue = DeliveryTermRules.Describe(term),
                ActorReference = _user.Actor(ReservationRules.MaxActorLength),
                OccurredAt = now,
                RequestId = request.RequestId,
            });
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gelijktijdig: dezelfde opdracht al opgeslagen (unieke requestId), of de order is intussen veranderd.
                _db.ChangeTracker.Clear();
                var saved = await _db.SalesOrderAuditEvents.AsNoTracking()
                    .Where(e => e.RequestId == request.RequestId && e.SalesOrderId == salesOrderId)
                    .Select(e => new { e.EventType, e.NewValue }).FirstOrDefaultAsync(cancellationToken);
                if (saved != null)
                {
                    return IsSameTermChange(saved.EventType, saved.NewValue, term)
                        ? await GetAsync(salesOrderId, cancellationToken)
                        : ServiceResult<SalesOrderDto>.Conflict($"RequestId {request.RequestId} was already used for a different change.");
                }
                return ServiceResult<SalesOrderDto>.Conflict($"Sales order {salesOrderId} changed in the meantime; reload it and decide again.");
            }
            _db.ChangeTracker.Clear();
            return await GetAsync(salesOrderId, cancellationToken);
        }

        private static bool IsSameTermChange(string eventType, string newValue, DeliveryTermRules.DeliveryTerm term) =>
            eventType == ReservationRules.DeliveryTermChangedEvent && newValue == DeliveryTermRules.Describe(term);
    }
}
