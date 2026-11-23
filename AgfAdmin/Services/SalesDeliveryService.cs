using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Deellevering (feedback 22 november): een nieuwe levering van een open verkooporder met een nieuwe verkoop-partij (V-nummer). De
    // gekozen actieve reserveringen gaan mee (ook geen: dan alleen wat nog niet gereserveerd is, dat staat altijd op de laatste levering).
    // Eén save met een auditregel; de laadbon en transportopdracht van beide leveringen ververst de controller daarna (RefreshLoadingSlip).
    public sealed class SalesDeliveryService(AppDbContext db, ICurrentUser user = null)
    {
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;

        public async Task<ServiceResult<SalesOrderDto>> CreateAsync(int orderId, CreateSalesDeliveryDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            if (request?.RequestId == null || request.RequestId == Guid.Empty)
            {
                errors["requestId"] = new[] { "A client-generated, non-empty requestId is required." };
            }
            var reservationIds = (request?.ReservationIds ?? new List<int>()).Distinct().OrderBy(id => id).ToList();
            if (reservationIds.Count > SalesDeliveryRules.MaxReservationsPerDelivery)
            {
                errors["reservationIds"] = new[] { $"At most {SalesDeliveryRules.MaxReservationsPerDelivery} reservations per delivery." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<SalesOrderDto>.Validation(errors);
            }
            var requestedPartij = string.IsNullOrWhiteSpace(request.PartijNumber) ? null : request.PartijNumber.Trim();

            for (var attempt = 1; ; attempt++)
            {
                // Herhaling van dezelfde aanvraag: dezelfde levering, geen tweede.
                var replay = await db.SalesDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.RequestId == request.RequestId, cancellationToken);
                if (replay != null)
                {
                    return replay.SalesOrderId == orderId && await SameRequestAsync(replay, reservationIds, requestedPartij, cancellationToken)
                        ? await OrderAsync(orderId, ServiceResultStatus.Ok, cancellationToken)
                        : ServiceResult<SalesOrderDto>.Conflict($"RequestId {request.RequestId} was already used for another delivery.");
                }

                var order = await db.SalesOrders.Include(o => o.Deliveries).ThenInclude(d => d.Partij)
                    .Include(o => o.Lines).ThenInclude(l => l.Reservations)
                    .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
                if (order == null)
                {
                    return ServiceResult<SalesOrderDto>.NotFound($"Sales order {orderId} does not exist.");
                }
                if (order.Status != ReservationRules.OrderStatusOpen)
                {
                    return ServiceResult<SalesOrderDto>.Conflict($"Sales order {order.OrderNumber} is cancelled; it gets no new delivery.");
                }
                var reservations = order.Lines.SelectMany(l => l.Reservations).Where(r => reservationIds.Contains(r.Id)).ToList();
                var unknown = reservationIds.Except(reservations.Select(r => r.Id)).ToList();
                if (unknown.Count > 0)
                {
                    return ServiceResult<SalesOrderDto>.Validation(new Dictionary<string, string[]> { ["reservationIds"] = new[] { $"Reservation {unknown[0]} does not belong to sales order {order.OrderNumber}." } });
                }
                if (reservations.FirstOrDefault(r => r.Status != ReservationRules.StatusActive) is { } done)
                {
                    return ServiceResult<SalesOrderDto>.Conflict($"Reservation {done.Id} is {done.Status}; only an active reservation goes with a new delivery.");
                }

                var built = await new PartijService(db, _user).BuildNewAsync<SalesOrderDto>(request.PartijNumber, "partijNumber", PartijRules.KindSales, cancellationToken);
                if (built.Refused != null)
                {
                    return built.Refused;
                }
                var now = DateTime.UtcNow;
                var actor = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
                var partij = built.Partij;
                partij.SalesOrder = order;
                var delivery = new SalesDelivery
                {
                    RequestId = request.RequestId, SalesOrder = order, Sequence = order.Deliveries.Max(d => d.Sequence) + 1, Partij = partij, CreatedAt = now, CreatedBy = actor,
                };
                partij.SalesDelivery = delivery;
                db.Partijen.Add(partij);
                db.SalesDeliveries.Add(delivery);
                foreach (var reservation in reservations)
                {
                    reservation.SalesDelivery = delivery;
                }
                order.UpdatedAt = now;
                db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
                {
                    SalesOrder = order, EventType = SalesDeliveryRules.DeliveryCreatedEvent, RequestId = request.RequestId, ActorReference = actor, OccurredAt = now,
                    NewValue = JsonSerializer.Serialize(new { delivery.Sequence, Partij = partij.Number, ReservationIds = reservationIds, RequestedPartij = requestedPartij }),
                });
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    return await OrderAsync(orderId, ServiceResultStatus.Created, cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return ServiceResult<SalesOrderDto>.Conflict($"Sales order {order.OrderNumber} was changed at the same time; reload and try again.");
                }
                catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < PartijService.MaxNumberAttempts)
                {
                    // Een gelijktijdige deellevering (volgnummer of V-nummer) of dezelfde aanvraag twee keer: opnieuw, met de herhaling eerst.
                    db.ChangeTracker.Clear();
                }
            }
        }

        // Dezelfde aanvraag: dezelfde reserveringen en hetzelfde aangevraagde V-nummer (leeg = automatisch), uit de auditregel van die levering.
        private async Task<bool> SameRequestAsync(SalesDelivery delivery, List<int> reservationIds, string requestedPartij, CancellationToken cancellationToken)
        {
            var audit = await db.SalesOrderAuditEvents.AsNoTracking().SingleOrDefaultAsync(e => e.RequestId == delivery.RequestId, cancellationToken);
            if (audit?.NewValue == null) return false;
            using var json = JsonDocument.Parse(audit.NewValue);
            var stored = json.RootElement.TryGetProperty("ReservationIds", out var ids) ? ids.EnumerateArray().Select(i => i.GetInt32()).OrderBy(i => i).ToList() : new List<int>();
            var storedPartij = json.RootElement.TryGetProperty("RequestedPartij", out var number) && number.ValueKind == JsonValueKind.String ? number.GetString() : null;
            var samePartij = storedPartij == null || requestedPartij == null
                ? storedPartij == requestedPartij
                : PartijRules.NumberKey(storedPartij) == PartijRules.NumberKey(requestedPartij);
            return samePartij && stored.SequenceEqual(reservationIds);
        }

        private async Task<ServiceResult<SalesOrderDto>> OrderAsync(int orderId, ServiceResultStatus status, CancellationToken cancellationToken)
        {
            var result = await new SalesOrderService(db, _user).GetAsync(orderId, cancellationToken);
            return result.Status != ServiceResultStatus.Ok ? result
                : status == ServiceResultStatus.Created ? ServiceResult<SalesOrderDto>.Created(result.Value) : result;
        }
    }
}
