using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Voorraadlocaties (feedback 22 november): lijst, toevoegen en aanpassen. Nooit verwijderen (inactief zetten); de soort ligt vast zodra er
    // een pallet of inkooporder naar wijst (guard-regel 40). Wie wijzigt, komt uit CurrentUser.
    public sealed class StockLocationService
    {
        private readonly AppDbContext _db;
        private readonly ICurrentUser _user;

        public StockLocationService(AppDbContext db, ICurrentUser currentUser = null)
        {
            _db = db;
            _user = currentUser ?? CurrentUser.Default;
        }

        public async Task<List<StockLocationDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var locations = await _db.StockLocations.AsNoTracking().OrderBy(l => l.Name).ToListAsync(cancellationToken);
            var used = await UsedAsync(locations.Select(l => l.Id).ToList(), cancellationToken);
            return locations.Select(l => ToDto(l, used.Contains(l.Id))).ToList();
        }

        public async Task<ServiceResult<StockLocationDto>> CreateAsync(SaveStockLocationDto request, CancellationToken cancellationToken = default)
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Invalid(errors);
            var name = request.Name.Trim();
            if (await _db.StockLocations.AnyAsync(l => l.Name == name, cancellationToken))
            {
                return Invalid(new() { ["name"] = new() { "A stock location with this name already exists." } });
            }
            var now = DateTime.UtcNow;
            var location = new StockLocation
            {
                Name = name, Kind = request.Kind, Address = Text(request.Address), CountryCode = Text(request.CountryCode)?.ToUpperInvariant(), IsActive = request.IsActive ?? true,
                CreatedAt = now, CreatedBy = Actor(), UpdatedAt = now, UpdatedBy = Actor(),
            };
            _db.StockLocations.Add(location);
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<StockLocationDto>.Created(ToDto(location, false));
        }

        public async Task<ServiceResult<StockLocationDto>> UpdateAsync(int id, SaveStockLocationDto request, CancellationToken cancellationToken = default)
        {
            var errors = Validate(request);
            if (request?.ExpectedVersion == null) (errors.TryGetValue("expectedVersion", out var list) ? list : errors["expectedVersion"] = new()).Add("The version as shown is required.");
            if (errors.Count > 0) return Invalid(errors);
            var location = await _db.StockLocations.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
            if (location == null) return ServiceResult<StockLocationDto>.NotFound($"Stock location {id} does not exist.");
            if (location.Version != request.ExpectedVersion) return ServiceResult<StockLocationDto>.Conflict("This stock location has been changed in the meantime; reload it first.");
            var name = request.Name.Trim();
            if (await _db.StockLocations.AnyAsync(l => l.Id != id && l.Name == name, cancellationToken))
            {
                return Invalid(new() { ["name"] = new() { "A stock location with this name already exists." } });
            }
            var inUse = (await UsedAsync(new List<int> { id }, cancellationToken)).Contains(id);
            var leaves = (request.IsActive ?? location.IsActive) == false || location.Kind != request.Kind;
            if (leaves && location.IsActive && location.Kind == StockLocationRules.KindInTransit
                && !await _db.StockLocations.AnyAsync(l => l.Id != id && l.IsActive && l.Kind == StockLocationRules.KindInTransit, cancellationToken))
            {
                return Invalid(new() { [request.IsActive == false ? "isActive" : "kind"] = new() { "Onderweg / direct naar klant is always available: this is the last active location of that kind." } });
            }
            if (leaves && await _db.CompanySettings.AnyAsync(c => c.DefaultReceiveLocationId == id, cancellationToken))
            {
                return Invalid(new() { [request.IsActive == false ? "isActive" : "kind"] = new() { "This is the default receive location; choose another default first." } });
            }
            if (inUse && location.Kind != request.Kind)
            {
                return Invalid(new() { ["kind"] = new() { "Pallets or purchase orders use this location, so its kind is fixed." } });
            }
            (location.Name, location.Kind, location.Address, location.CountryCode, location.IsActive) =
                (name, request.Kind, Text(request.Address), Text(request.CountryCode)?.ToUpperInvariant(), request.IsActive ?? location.IsActive);
            (location.UpdatedAt, location.UpdatedBy) = (DateTime.UtcNow, Actor());
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<StockLocationDto>.Conflict("This stock location has been changed in the meantime; reload it first.");
            }
            return ServiceResult<StockLocationDto>.Ok(ToDto(location, inUse));
        }

        // De ontvangstlocatie van een nieuwe inkooporder: de standaard (alleen als die nog een actieve eigen loods is) en het loodsadres.
        public async Task<ReceiveDefaultsDto> ReceiveDefaultsAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _db.CompanySettings.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.DefaultReceiveLocationId, c.LoadingSlipWarehouseAddress }).FirstOrDefaultAsync(cancellationToken);
            var id = settings?.DefaultReceiveLocationId;
            var valid = id != null && await _db.StockLocations.AnyAsync(l => l.Id == id && l.IsActive && l.Kind == StockLocationRules.KindOwnWarehouse, cancellationToken);
            return new ReceiveDefaultsDto { DefaultReceiveLocationId = valid ? id : null, OwnWarehouseAddress = Text(settings?.LoadingSlipWarehouseAddress) };
        }

        // Instellingen › Voorraadlocaties: de standaard ontvangstlocatie (leeg = de ingebouwde Eigen loods, of een actieve eigen loods).
        public async Task<ServiceResult<ReceiveDefaultsDto>> SaveReceiveDefaultsAsync(SaveReceiveDefaultsDto request, CancellationToken cancellationToken = default)
        {
            var id = request?.DefaultReceiveLocationId;
            if (id != null && !await _db.StockLocations.AnyAsync(l => l.Id == id && l.IsActive && l.Kind == StockLocationRules.KindOwnWarehouse, cancellationToken))
            {
                return ServiceResult<ReceiveDefaultsDto>.Validation(new Dictionary<string, string[]>
                {
                    ["defaultReceiveLocationId"] = new[] { "Choose your own warehouse or an active location of the kind own warehouse." }
                });
            }
            var settings = await _db.CompanySettings.OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
            if (settings == null)
            {
                settings = new CompanySettings();
                _db.CompanySettings.Add(settings);
            }
            settings.DefaultReceiveLocationId = id;
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<ReceiveDefaultsDto>.Ok(await ReceiveDefaultsAsync(cancellationToken));
        }

        // Bij het opstarten (Program.cs), idempotent: Onderweg / direct naar klant is er altijd. Is er geen actieve InTransit-locatie, dan
        // wordt een inactieve met de standaardnaam weer actief, of komt er een nieuwe. Bestaande InTransit-locaties blijven zoals ze zijn.
        // In PostgreSQL met een transactie-slot, zodat twee API's die tegelijk starten er niet elk één maken.
        public static async Task<bool> EnsureInTransitAsync(AppDbContext db, CancellationToken cancellationToken = default)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EnsureLockClass}, 1)", cancellationToken);
            }
            if (await db.StockLocations.AnyAsync(l => l.IsActive && l.Kind == StockLocationRules.KindInTransit, cancellationToken))
            {
                return false;
            }
            var now = DateTime.UtcNow;
            var standard = await db.StockLocations.SingleOrDefaultAsync(l => l.Name == StockLocationRules.StandardInTransitName, cancellationToken);
            if (standard != null && standard.Kind == StockLocationRules.KindInTransit)
            {
                (standard.IsActive, standard.UpdatedAt, standard.UpdatedBy) = (true, now, StockLocationRules.SystemActor);
            }
            else
            {
                var name = StockLocationRules.StandardInTransitName;
                for (var n = 2; await db.StockLocations.AnyAsync(l => l.Name == name, cancellationToken); n++) name = $"{StockLocationRules.StandardInTransitName} ({n})";
                db.StockLocations.Add(new StockLocation
                {
                    Name = name, Kind = StockLocationRules.KindInTransit, IsActive = true,
                    CreatedAt = now, CreatedBy = StockLocationRules.SystemActor, UpdatedAt = now, UpdatedBy = StockLocationRules.SystemActor,
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        private const int EnsureLockClass = 47047;

        // Locaties waar een pallet of inkooporder naar wijst.
        private async Task<HashSet<int>> UsedAsync(List<int> ids, CancellationToken cancellationToken)
        {
            var pallets = await _db.Pallets.AsNoTracking().Where(p => p.StockLocationId != null && ids.Contains(p.StockLocationId.Value)).Select(p => p.StockLocationId.Value).Distinct().ToListAsync(cancellationToken);
            var orders = await _db.SupplyCommitments.AsNoTracking().Where(c => c.ReceiveLocationId != null && ids.Contains(c.ReceiveLocationId.Value)).Select(c => c.ReceiveLocationId.Value).Distinct().ToListAsync(cancellationToken);
            return pallets.Concat(orders).ToHashSet();
        }

        private static Dictionary<string, List<string>> Validate(SaveStockLocationDto request)
        {
            var errors = new Dictionary<string, List<string>>();
            void Add(string key, string message) { if (!errors.TryGetValue(key, out var list)) errors[key] = list = new(); list.Add(message); }
            if (request == null) { Add("request", "A request body is required."); return errors; }
            if (string.IsNullOrWhiteSpace(request.Name)) Add("name", "A name is required.");
            else if (request.Name.Trim().Length > StockLocationRules.MaxNameLength) Add("name", $"This field may contain at most {StockLocationRules.MaxNameLength} characters.");
            if (!StockLocationRules.IsKind(request.Kind)) Add("kind", $"Kind must be one of: {string.Join(", ", StockLocationRules.Kinds)}.");
            if (Text(request.Address)?.Length > StockLocationRules.MaxAddressLength) Add("address", $"This field may contain at most {StockLocationRules.MaxAddressLength} characters.");
            var country = Text(request.CountryCode)?.ToUpperInvariant();
            if (country != null && !CountryRules.IsCode(country)) Add("countryCode", "Choose a country from the list.");
            return errors;
        }

        private string Actor() => _user.Actor(100);
        private static string Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static ServiceResult<StockLocationDto> Invalid(Dictionary<string, List<string>> errors) =>
            ServiceResult<StockLocationDto>.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        public static StockLocationDto ToDto(StockLocation l, bool inUse) => new()
        {
            Id = l.Id, Name = l.Name, Kind = l.Kind, Address = l.Address, CountryCode = l.CountryCode, IsActive = l.IsActive, InUse = inUse, Version = l.Version,
        };
    }
}
