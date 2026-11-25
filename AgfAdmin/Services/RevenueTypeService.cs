using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1: de omzetsoorten onder Beheer. Een korte lijst (standaard "Omzet diensten") voor dienst- en vrije regels; de koppeling
    // met een grootboekrekening in Exact vult later alleen ExternalCode. Nooit verwijderen (regel 44): inactief zetten. Precies één actieve
    // standaard; de standaard kan niet inactief worden zolang er geen andere is.
    public sealed class RevenueTypeService(AppDbContext db)
    {
        public async Task<List<RevenueTypeDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
            (await db.RevenueTypes.AsNoTracking().Where(r => includeInactive || r.IsActive).ToListAsync(cancellationToken))
                .OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(Dto).ToList();

        public async Task<ServiceResult<RevenueTypeDto>> CreateAsync(SaveRevenueTypeDto request, CancellationToken cancellationToken = default)
        {
            var (name, code, errors) = await ValidateAsync(request, null, cancellationToken);
            if (request?.IsActive == false && request.IsDefault == true)
            {
                errors["isDefault"] = new[] { "The default revenue type is active." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<RevenueTypeDto>.Validation(errors);
            }
            var type = new RevenueType { Name = name, ExternalCode = code, IsActive = request.IsActive ?? true, IsDefault = false };
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.RevenueTypes.Add(type);
            await db.SaveChangesAsync(cancellationToken);
            if (request.IsDefault == true)
            {
                await MakeDefaultAsync(type, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<RevenueTypeDto>.Created(Dto(type));
        }

        public async Task<ServiceResult<RevenueTypeDto>> UpdateAsync(int id, SaveRevenueTypeDto request, CancellationToken cancellationToken = default)
        {
            var type = await db.RevenueTypes.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (type == null)
            {
                return ServiceResult<RevenueTypeDto>.NotFound($"Revenue type {id} does not exist.");
            }
            var (name, code, errors) = await ValidateAsync(request, id, cancellationToken);
            if (request?.ExpectedVersion == null)
            {
                errors["expectedVersion"] = new[] { "The version you saw is required." };
            }
            var active = request?.IsActive ?? type.IsActive;
            if (type.IsDefault && !active)
            {
                errors["isActive"] = new[] { "This is the default revenue type: make another one the default first." };
            }
            if (request?.IsDefault == false && type.IsDefault)
            {
                errors["isDefault"] = new[] { "Make another revenue type the default instead." };
            }
            if (request?.IsDefault == true && !active)
            {
                errors["isDefault"] = new[] { "The default revenue type is active." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<RevenueTypeDto>.Validation(errors);
            }
            if (type.Version != request.ExpectedVersion)
            {
                return ServiceResult<RevenueTypeDto>.Conflict($"Revenue type {type.Name} was changed in the meantime. Reload and try again.");
            }
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            type.Name = name;
            type.ExternalCode = code;
            type.IsActive = active;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                if (request.IsDefault == true && !type.IsDefault)
                {
                    await MakeDefaultAsync(type, cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<RevenueTypeDto>.Conflict($"Revenue type {type.Name} was changed in the meantime. Reload and try again.");
            }
            return ServiceResult<RevenueTypeDto>.Ok(Dto(type));
        }

        // Eerst de oude standaard uit, dan de nieuwe aan (twee saves in één transactie: de unieke index op de standaard staat het andersom niet toe).
        private async Task MakeDefaultAsync(RevenueType type, CancellationToken cancellationToken)
        {
            foreach (var old in await db.RevenueTypes.Where(r => r.IsDefault && r.Id != type.Id).ToListAsync(cancellationToken))
            {
                old.IsDefault = false;
            }
            await db.SaveChangesAsync(cancellationToken);
            type.IsDefault = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        private async Task<(string Name, string Code, Dictionary<string, string[]> Errors)> ValidateAsync(SaveRevenueTypeDto request, int? id, CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, string[]>();
            var name = request?.Name?.Trim();
            var code = string.IsNullOrWhiteSpace(request?.ExternalCode) ? null : request.ExternalCode.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > SalesInvoiceRules.MaxRevenueTypeNameLength)
            {
                errors["name"] = new[] { $"A name of 1 to {SalesInvoiceRules.MaxRevenueTypeNameLength} characters is required." };
            }
            else
            {
                var lower = name.ToLower();
                if (await db.RevenueTypes.AnyAsync(r => r.Id != id && r.Name.ToLower() == lower, cancellationToken))
                {
                    errors["name"] = new[] { $"Revenue type {name} already exists." };
                }
            }
            if (code != null && code.Length > SalesInvoiceRules.MaxExternalCodeLength)
            {
                errors["externalCode"] = new[] { $"This field may contain at most {SalesInvoiceRules.MaxExternalCodeLength} characters." };
            }
            return (name, code, errors);
        }

        private static RevenueTypeDto Dto(RevenueType r) => new(r.Id, r.Name, r.ExternalCode, r.IsActive, r.IsDefault, r.Version);
    }
}
