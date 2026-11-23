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
