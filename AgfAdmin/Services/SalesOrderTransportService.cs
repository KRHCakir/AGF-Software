using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Transport op een verkooporder (documenten deel 2). Na opslaan maakt het filter op de controller de transportopdracht of een nieuwe
    // versie (als de order bevestigd is). Wie en wanneer komt uit CurrentUser.
    public sealed class SalesOrderTransportService(AppDbContext db, ICurrentUser user = null)
    {
        public const string CancelledCode = "cancelled";
        public const string TransportOrderExistsCode = "transportOrderExists";
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;

        public async Task<ServiceResult<SalesOrderTransportDto>> GetAsync(int orderId, CancellationToken cancellationToken = default)
        {
            var order = await LoadAsync(orderId, tracking: false, cancellationToken);
            return order == null ? NotFound(orderId) : ServiceResult<SalesOrderTransportDto>.Ok(await ToDtoAsync(order, cancellationToken));
        }

        public async Task<ServiceResult<SalesOrderTransportDto>> SaveAsync(int orderId, SaveSalesOrderTransportDto request, CancellationToken cancellationToken = default)
        {
            var order = await LoadAsync(orderId, tracking: true, cancellationToken);
            if (order == null) return NotFound(orderId);
            if (order.Status == ReservationRules.OrderStatusCancelled)
            {
                return ServiceResult<SalesOrderTransportDto>.Conflict($"Sales order {order.OrderNumber} is cancelled.", new { code = CancelledCode });
            }
            var existing = order.Transport;
            var errors = new Dictionary<string, List<string>>();
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return Validation(errors);
            }
            if (existing != null && request.ExpectedVersion == null) ValidationErrors.Add(errors, "expectedVersion", "Send the version you changed (expectedVersion).");
            if (existing != null && request.ExpectedVersion != null && request.ExpectedVersion != existing.Version)
            {
                return ServiceResult<SalesOrderTransportDto>.Conflict("The transport was changed by someone else; reload and try again.", new { code = "stale", version = existing.Version });
            }

            var mode = SalesTransportRules.Modes.FirstOrDefault(m => string.Equals(m, request.Mode?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (mode == null) ValidationErrors.Add(errors, "mode", "Choose a carrier (Carrier) or own transport / customer pick-up (Own).");
            int? carrierId = null;
            if (mode == SalesTransportRules.ModeCarrier)
            {
                if (request.CarrierRelationId == null)
                {
                    ValidationErrors.Add(errors, "carrierRelationId", "Choose the carrier.");
                }
                else
                {
                    var carrier = await db.Relations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.CarrierRelationId, cancellationToken);
                    var unchanged = existing?.CarrierRelationId == request.CarrierRelationId;
                    if (carrier == null || !carrier.IsCarrier) ValidationErrors.Add(errors, "carrierRelationId", "This relation is not a carrier; give it the carrier role first.");
                    else if (!carrier.IsActive && !unchanged) ValidationErrors.Add(errors, "carrierRelationId", $"Carrier {carrier.Name} is inactive.");
                    else carrierId = carrier.Id;
                }
            }

            var loadingPlace = ValidationErrors.OptionalText(request.LoadingPlace, SalesTransportRules.MaxPlaceLength, "loadingPlace", errors);
            var unloadingPlace = ValidationErrors.OptionalText(request.UnloadingPlace, SalesTransportRules.MaxPlaceLength, "unloadingPlace", errors);
            RelationAddress unloadingAddress = null;
            if (request.UnloadingAddressId != null)
            {
                unloadingAddress = order.Customer?.Relation?.Addresses.SingleOrDefault(a => a.Id == request.UnloadingAddressId);
                if (unloadingAddress == null || (!unloadingAddress.IsActive && existing?.UnloadingAddressId != request.UnloadingAddressId))
                {
                    ValidationErrors.Add(errors, "unloadingAddressId", "Choose one of the customer's addresses, or fill in a different address.");
                }
            }
            // Leeg laat de laadtijd van de order staan (die geldt ook voor de laadbon); alleen ClearLoadAt wist haar.
            var loadAt = request.ClearLoadAt ? null : Utc(request.LoadAt) ?? order.LoadAt;
            var unloadDate = request.UnloadDate?.Date;
            if (unloadDate != null && loadAt != null && DateOnly.FromDateTime(unloadDate.Value) < db.BusinessCalendar.DateOf(loadAt.Value))
            {
                ValidationErrors.Add(errors, "unloadDate", "Unloading cannot be before loading.");
            }
            var temperature = request.TemperatureC;
            if (temperature != null && (temperature < SalesTransportRules.MinTemperatureC || temperature > SalesTransportRules.MaxTemperatureC || decimal.Round(temperature.Value, 1) != temperature))
            {
                ValidationErrors.Add(errors, "temperatureC", $"Temperature from {SalesTransportRules.MinTemperatureC} to {SalesTransportRules.MaxTemperatureC} °C, with at most one decimal.");
            }
            var loadingReference = ValidationErrors.OptionalText(request.LoadingReference, SalesTransportRules.MaxReferenceLength, "loadingReference", errors);
            var unloadingReference = ValidationErrors.OptionalText(request.UnloadingReference, SalesTransportRules.MaxReferenceLength, "unloadingReference", errors);
            var remarks = ValidationErrors.OptionalText(request.Remarks, SalesTransportRules.MaxRemarksLength, "remarks", errors);
            var palletKind = ValidationErrors.OptionalText(request.PalletKind, SalesTransportRules.MaxPalletKindLength, "palletKind", errors);
            if (errors.Count > 0) return Validation(errors);

            var now = DateTime.UtcNow;
            var actor = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
            var transport = existing ?? new SalesOrderTransport { SalesOrderId = order.Id, CreatedAt = now, CreatedBy = actor };
            transport.Mode = mode;
            transport.CarrierRelationId = carrierId;
            transport.LoadingPlace = loadingPlace;
            transport.UnloadingAddressId = unloadingAddress?.Id;
            transport.UnloadingPlace = unloadingPlace;
            transport.UnloadDate = unloadDate == null ? null : DateTime.SpecifyKind(unloadDate.Value, DateTimeKind.Utc);
            transport.TemperatureC = temperature;
            transport.LoadingReference = loadingReference;
            transport.UnloadingReference = unloadingReference;
            transport.Remarks = remarks;
            transport.PalletKind = palletKind;
            transport.UpdatedAt = now;
            transport.UpdatedBy = actor;
            if (existing == null) db.SalesOrderTransports.Add(transport);
            // Laden op is één waarde voor de loods en de vervoerder: dezelfde als op de laadbon.
            if (order.LoadAt != loadAt)
            {
                order.LoadAt = loadAt;
                order.UpdatedAt = now;
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException || (exception is DbUpdateException update && DbErrors.IsUniqueViolation(update)))
            {
                return ServiceResult<SalesOrderTransportDto>.Conflict("The transport was changed by someone else; reload and try again.", new { code = "stale" });
            }
            db.ChangeTracker.Clear();
            return ServiceResult<SalesOrderTransportDto>.Ok(await ToDtoAsync(await LoadAsync(orderId, tracking: false, cancellationToken), cancellationToken));
        }

        // Weghalen kan zolang er geen transportopdracht is. Met de versie die de gebruiker zag (expectedVersion): een gelijktijdige wijziging
        // gaat niet verloren. Het weghalen raakt ook de orderrij (concurrency-token): een gelijktijdige bevestiging, die de orderrij ook
        // wijzigt, botst dan op de orderversie, zodat nooit een transportopdracht zonder transport ontstaat.
        public async Task<ServiceResult<SalesOrderTransportDto>> DeleteAsync(int orderId, int? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            var order = await LoadAsync(orderId, tracking: true, cancellationToken);
            if (order == null) return NotFound(orderId);
            if (await db.SalesOrderDocuments.AnyAsync(d => d.SalesOrderId == orderId && d.Kind == SalesOrderDocumentRules.KindTransportOrder, cancellationToken))
            {
                return ServiceResult<SalesOrderTransportDto>.Conflict("There is already a transport order for this sales order; change the transport instead.", new { code = TransportOrderExistsCode });
            }
            if (order.Transport != null)
            {
                if (expectedVersion != null && expectedVersion != order.Transport.Version)
                {
                    return ServiceResult<SalesOrderTransportDto>.Conflict("The transport was changed by someone else; reload and try again.", new { code = "stale", version = order.Transport.Version });
                }
                db.SalesOrderTransports.Remove(order.Transport);
                order.UpdatedAt = DateTime.UtcNow;
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return ServiceResult<SalesOrderTransportDto>.Conflict("The sales order was changed meanwhile (for example confirmed); reload and try again.", new { code = "stale" });
                }
            }
            db.ChangeTracker.Clear();
            return ServiceResult<SalesOrderTransportDto>.Ok(await ToDtoAsync(await LoadAsync(orderId, tracking: false, cancellationToken), cancellationToken));
        }

        public async Task<TransportOrderSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            var settings = await SettingsAsync(cancellationToken);
            return new TransportOrderSettingsDto { Language = settings.TransportOrderLanguage, PalletKind = settings.TransportOrderPalletKind };
        }

        // Geldt voor nieuwe versies; een bestaande versie houdt haar snapshot.
        public async Task<ServiceResult<TransportOrderSettingsDto>> UpdateSettingsAsync(TransportOrderSettingsDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var language = request?.Language?.Trim().ToLowerInvariant();
            if (language == null || !SalesOrderDocumentRules.Languages.Contains(language))
            {
                ValidationErrors.Add(errors, "language", $"Language must be one of: {string.Join(", ", SalesOrderDocumentRules.Languages)}.");
            }
            var palletKind = ValidationErrors.OptionalText(request?.PalletKind, SalesTransportRules.MaxPalletKindLength, "palletKind", errors) ?? SalesTransportRules.DefaultPalletKind;
            if (errors.Count > 0) return ServiceResult<TransportOrderSettingsDto>.Validation(ValidationErrors.ToArrays(errors));
            var settings = await db.CompanySettings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (settings == null) db.CompanySettings.Add(settings = new CompanySettings());
            settings.TransportOrderLanguage = language;
            settings.TransportOrderPalletKind = palletKind;
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<TransportOrderSettingsDto>.Ok(await GetSettingsAsync(cancellationToken));
        }

        private async Task<SalesOrderTransportDto> ToDtoAsync(SalesOrder order, CancellationToken cancellationToken)
        {
            var settings = await SettingsAsync(cancellationToken);
            var transport = order.Transport;
            var addresses = order.Customer?.Relation?.Addresses
                .Where(a => (a.IsActive || a.Id == transport?.UnloadingAddressId) && (a.Purpose == AddressPurpose.Delivery || a.Purpose == AddressPurpose.Main))
                .OrderByDescending(a => a.Purpose == AddressPurpose.Delivery).ThenByDescending(a => a.IsDefault).ThenBy(a => a.SortOrder).ThenBy(a => a.Id)
                .Select(a => new TransportAddressOptionDto { Id = a.Id, Purpose = a.Purpose, Text = TransportOrderBuilder.AddressText(a), IsDefault = a.IsDefault })
                .ToList() ?? new List<TransportAddressOptionDto>();
            return new SalesOrderTransportDto
            {
                SalesOrderId = order.Id, Exists = transport != null, Mode = transport?.Mode, CarrierRelationId = transport?.CarrierRelationId,
                CarrierName = transport?.CarrierRelation?.Name, CarrierAddress = TransportOrderBuilder.AddressText(TransportOrderBuilder.MainAddress(transport?.CarrierRelation)),
                LoadingPlace = transport?.LoadingPlace, DefaultLoadingPlace = TransportOrderBuilder.LoadingPlace(null, settings),
                UnloadingAddressId = transport?.UnloadingAddressId, UnloadingPlace = transport?.UnloadingPlace,
                DefaultUnloadingPlace = TransportOrderBuilder.UnloadingPlace(order, new SalesOrderTransport { UnloadingAddress = transport?.UnloadingAddress }),
                UnloadingAddresses = addresses,
                LoadAt = order.LoadAt == null ? null : DateTime.SpecifyKind(order.LoadAt.Value, DateTimeKind.Utc),
                UnloadDate = transport?.UnloadDate == null ? null : DateTime.SpecifyKind(transport.UnloadDate.Value.Date, DateTimeKind.Utc),
                TemperatureC = transport?.TemperatureC, LoadingReference = transport?.LoadingReference, UnloadingReference = transport?.UnloadingReference,
                DefaultUnloadingReference = order.CustomerReference, Remarks = transport?.Remarks, PalletKind = transport?.PalletKind,
                DefaultPalletKind = settings.TransportOrderPalletKind, Version = transport?.Version ?? 0,
                HasTransportOrder = await db.SalesOrderDocuments.AsNoTracking().AnyAsync(d => d.SalesOrderId == order.Id && d.Kind == SalesOrderDocumentRules.KindTransportOrder, cancellationToken),
            };
        }

        private Task<SalesOrder> LoadAsync(int orderId, bool tracking, CancellationToken cancellationToken)
        {
            var query = db.SalesOrders
                .Include(o => o.Customer).ThenInclude(c => c.Relation).ThenInclude(r => r.Addresses)
                .Include(o => o.Transport).ThenInclude(t => t.CarrierRelation).ThenInclude(r => r.Addresses)
                .Include(o => o.Transport).ThenInclude(t => t.UnloadingAddress)
                .AsSplitQuery();
            return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        }

        private async Task<CompanySettings> SettingsAsync(CancellationToken cancellationToken) =>
            await db.CompanySettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken) ?? new CompanySettings();

        private static DateTime? Utc(DateTime? value) => value == null ? null : value.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value.Value.ToUniversalTime();

        private static ServiceResult<SalesOrderTransportDto> NotFound(int orderId) => ServiceResult<SalesOrderTransportDto>.NotFound($"Sales order {orderId} does not exist.");

        private static ServiceResult<SalesOrderTransportDto> Validation(Dictionary<string, List<string>> errors) =>
            ServiceResult<SalesOrderTransportDto>.Validation(ValidationErrors.ToArrays(errors));
    }
}
