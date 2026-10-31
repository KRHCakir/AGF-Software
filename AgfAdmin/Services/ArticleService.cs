using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Artikelbeheer op het bestaande model: Product (productsoort) + ProductVariant (artikel). Artikelgegevens zijn
    // standaarden voor nieuwe invoer; een wijziging raakt nooit bestaande documenten, inhoudsregels, voorraad of keuringen.
    public sealed class ArticleService
    {
        private readonly AppDbContext _db;

        public ArticleService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<ArticleDto>> ListAsync(CancellationToken cancellationToken = default) =>
            (await _db.ProductVariants.AsNoTracking().Include(v => v.Product).ToListAsync(cancellationToken))
                .Select(ToDto).OrderBy(a => a.ProductName, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.VariantName, StringComparer.CurrentCultureIgnoreCase).ToList();

        public async Task<ServiceResult<ArticleDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var variant = await _db.ProductVariants.AsNoTracking().Include(v => v.Product).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
            return variant == null ? ServiceResult<ArticleDto>.NotFound($"Article {id} does not exist.") : ServiceResult<ArticleDto>.Ok(ToDto(variant));
        }

        public async Task<ArticleReferenceDto> ReferenceAsync(CancellationToken cancellationToken = default) => new()
        {
            ProductNames = await _db.Products.AsNoTracking().OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(cancellationToken),
            PackagingUnits = SupplyRules.PackageUnits.ToList(),
            WeightMethods = WeightRules.Methods.ToList(),
            GoodsCodeSystems = ArticleRules.GoodsCodeSystems.ToList(),
            TradeClassSuggestions = ArticleRules.TradeClassSuggestions.ToList()
        };

        // "Herkomst opschonen": artikelen zonder land (oude, niet eenduidige herkomsttekst of leeg), met de landen die de tekst
        // zou kunnen zijn als voorstel. Er wordt nooit automatisch gekozen.
        public async Task<IReadOnlyList<OriginCleanupItemDto>> OriginCleanupAsync(CancellationToken cancellationToken = default) =>
            (await _db.ProductVariants.AsNoTracking().Include(v => v.Product).Where(v => v.OriginCountryCode == null).ToListAsync(cancellationToken))
                .Select(v => new OriginCleanupItemDto
                {
                    Id = v.Id,
                    Description = Describe(v),
                    ShortcutCode = Blank(v.ShortcutCode),
                    Origin = Blank(v.Origin),
                    Suggestions = CountryRules.Matches(v.Origin).ToList()
                })
                .OrderBy(i => i.Description, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        // Alleen het land van een artikel zetten (vanuit "Herkomst opschonen"); de rest van het artikel blijft zoals het is.
        public async Task<ServiceResult<ArticleDto>> SetOriginCountryAsync(int id, SetOriginCountryDto request, CancellationToken cancellationToken = default)
        {
            var country = CountryRules.Find(string.IsNullOrWhiteSpace(request?.OriginCountryCode) ? null : request.OriginCountryCode.Trim().ToUpperInvariant());
            if (country == null)
            {
                return ServiceResult<ArticleDto>.Validation(new Dictionary<string, string[]> { ["originCountryCode"] = new[] { "Choose the country of origin from the country list." } });
            }
            var variant = await _db.ProductVariants.Include(v => v.Product).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
            if (variant == null)
            {
                return ServiceResult<ArticleDto>.NotFound($"Article {id} does not exist.");
            }
            variant.OriginCountryCode = country.Code;
            variant.Origin = country.NameNl;
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<ArticleDto>.Ok(ToDto(variant));
        }

        public async Task<ServiceResult<ArticleDto>> CreateAsync(SaveArticleDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var values = await ValidateAsync(request, null, errors, cancellationToken);
            if (errors.Count > 0)
            {
                return ServiceResult<ArticleDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var product = await FindProductAsync(values.ProductName, cancellationToken) ?? new Product { Name = values.ProductName };
            var variant = new ProductVariant { Product = product, DefaultPrice = 0m };
            Apply(variant, values);
            _db.ProductVariants.Add(variant);
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<ArticleDto>.Created(ToDto(variant));
        }

        public async Task<ServiceResult<ArticleDto>> UpdateAsync(int id, SaveArticleDto request, CancellationToken cancellationToken = default)
        {
            var variant = await _db.ProductVariants.Include(v => v.Product).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
            if (variant == null)
            {
                return ServiceResult<ArticleDto>.NotFound($"Article {id} does not exist.");
            }
            var errors = new Dictionary<string, List<string>>();
            var values = await ValidateAsync(request, id, errors, cancellationToken);
            if (errors.Count > 0)
            {
                return ServiceResult<ArticleDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            if (!string.Equals(variant.Product.Name.Trim(), values.ProductName, StringComparison.OrdinalIgnoreCase))
            {
                // Productsoort gewijzigd: naar de bestaande soort met die naam, anders een nieuwe; is dit het enige artikel
                // van de oude soort, dan wordt die soort hernoemd (geen lege soorten achterlaten).
                var target = await FindProductAsync(values.ProductName, cancellationToken);
                var siblings = await _db.ProductVariants.CountAsync(v => v.ProductId == variant.ProductId && v.Id != id, cancellationToken);
                if (target != null)
                {
                    variant.Product = target;
                    variant.ProductId = target.Id;
                }
                else if (siblings == 0)
                {
                    variant.Product.Name = values.ProductName;
                }
                else
                {
                    variant.Product = new Product { Name = values.ProductName };
                }
            }
            else if (variant.Product.Name != values.ProductName)
            {
                variant.Product.Name = values.ProductName; // alleen schrijfwijze (spaties, hoofdletters)
            }
            Apply(variant, values);
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<ArticleDto>.Ok(ToDto(variant));
        }

        private Task<Product> FindProductAsync(string name, CancellationToken cancellationToken)
        {
            var lower = name.ToLowerInvariant();
            return _db.Products.FirstOrDefaultAsync(p => p.Name.ToLower() == lower, cancellationToken);
        }

        private sealed record Values(
            string ProductName, string VariantName, string ShortcutCode, string OriginCountryCode, string Origin, string Brand, string SizeCategory, string Packaging,
            string PackagingUnit, string Season, string Notes, string WeightMethod, decimal? NetKg, decimal? GrossKg, decimal? PackagesPerPallet,
            string GoodsCode, string GoodsCodeSystem, string TradeClass);

        private async Task<Values> ValidateAsync(SaveArticleDto request, int? selfId, Dictionary<string, List<string>> errors, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return null;
            }
            var productName = ValidationErrors.RequiredText(request.ProductName, ArticleRules.MaxNameLength, "productName", errors);
            var variantName = ValidationErrors.OptionalText(request.VariantName, ArticleRules.MaxNameLength, "variantName", errors) ?? "";

            var shortcut = ValidationErrors.OptionalText(request.ShortcutCode, ArticleRules.MaxShortcutLength, "shortcutCode", errors);
            if (shortcut != null)
            {
                if (!ArticleRules.IsShortcutCode(shortcut))
                {
                    ValidationErrors.Add(errors, "shortcutCode", "A search code consists of letters, digits, '.', '-' or '_' (max 32), without spaces.");
                }
                else
                {
                    var lower = shortcut.ToLowerInvariant();
                    var taken = await _db.ProductVariants.AsNoTracking().Include(v => v.Product)
                        .FirstOrDefaultAsync(v => v.ShortcutCode != null && v.ShortcutCode.ToLower() == lower && v.Id != selfId, cancellationToken);
                    if (taken != null)
                    {
                        ValidationErrors.Add(errors, "shortcutCode", $"Search code '{shortcut}' is already used by article #{taken.Id} ({Describe(taken)}).");
                    }
                }
            }

            // Herkomst is een land uit de vaste lijst (verplicht bij nieuw en wijzigen); de weergavetekst Origin wordt de Nederlandse
            // naam. Een vrije herkomsttekst (request.Origin) wordt niet meer overgenomen: alleen het land telt.
            var country = CountryRules.Find(string.IsNullOrWhiteSpace(request.OriginCountryCode) ? null : request.OriginCountryCode.Trim().ToUpperInvariant());
            if (country == null)
            {
                ValidationErrors.Add(errors, "originCountryCode", string.IsNullOrWhiteSpace(request.OriginCountryCode)
                    ? "Choose the country of origin from the country list."
                    : $"Country '{request.OriginCountryCode}' is not in the country list.");
            }
            var brand = ValidationErrors.OptionalText(request.Brand, ArticleRules.MaxTextLength, "brand", errors);
            var size = ValidationErrors.OptionalText(request.SizeCategory, ArticleRules.MaxTextLength, "sizeCategory", errors);
            var packaging = ValidationErrors.OptionalText(request.Packaging, ArticleRules.MaxTextLength, "packaging", errors);
            var packagingUnit = string.IsNullOrWhiteSpace(request.PackagingUnit) ? null : request.PackagingUnit.Trim();
            if (packagingUnit != null && !SupplyRules.IsPackageUnit(packagingUnit))
            {
                ValidationErrors.Add(errors, "packagingUnit", $"Packaging unit must be empty or one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
            }
            var season = ValidationErrors.OptionalText(request.Season, ArticleRules.MaxTextLength, "season", errors);
            var notes = ValidationErrors.OptionalText(request.Notes, ArticleRules.MaxNotesLength, "notes", errors);

            var method = string.IsNullOrWhiteSpace(request.WeightMethod) ? WeightRules.Weighed : request.WeightMethod.Trim();
            if (!WeightRules.IsWeightMethod(method))
            {
                ValidationErrors.Add(errors, "weightMethod", $"Weight method must be one of: {string.Join(", ", WeightRules.Methods)}.");
            }
            if (request.DefaultNetKgPerPackage != null && !SupplyRules.IsPositiveDecimal18_3(request.DefaultNetKgPerPackage.Value))
            {
                ValidationErrors.Add(errors, "defaultNetKgPerPackage", "Net kg per package must be positive and may have at most 3 decimals.");
            }
            if (request.DefaultGrossKgPerPackage != null && !SupplyRules.IsPositiveDecimal18_3(request.DefaultGrossKgPerPackage.Value))
            {
                ValidationErrors.Add(errors, "defaultGrossKgPerPackage", "Gross kg per package must be positive and may have at most 3 decimals.");
            }
            if (request.DefaultNetKgPerPackage != null && request.DefaultGrossKgPerPackage != null && request.DefaultGrossKgPerPackage < request.DefaultNetKgPerPackage)
            {
                ValidationErrors.Add(errors, "defaultGrossKgPerPackage", "Gross kg per package cannot be less than net kg per package.");
            }
            if (request.DefaultPackagesPerPallet != null && !SupplyRules.IsPositiveDecimal18_3(request.DefaultPackagesPerPallet.Value))
            {
                ValidationErrors.Add(errors, "defaultPackagesPerPallet", "Packages per pallet must be positive and may have at most 3 decimals.");
            }

            // Goederencode: als tekst, cijfers na normalisatie, lengte volgens het expliciete codestelsel.
            var goodsCode = ArticleRules.NormalizeGoodsCode(request.GoodsCode);
            var system = string.IsNullOrWhiteSpace(request.GoodsCodeSystem) ? null : request.GoodsCodeSystem.Trim().ToUpperInvariant();
            if (goodsCode != null && !ArticleRules.IsDigitsOnly(goodsCode))
            {
                ValidationErrors.Add(errors, "goodsCode", "A goods code consists of digits only; spaces and dots are allowed as separators, letters and other characters are not.");
            }
            else if (goodsCode != null)
            {
                if (system == null)
                {
                    ValidationErrors.Add(errors, "goodsCodeSystem", $"Choose the code system for the goods code: {string.Join(", ", ArticleRules.GoodsCodeSystems)}.");
                }
                else if (!ArticleRules.IsGoodsCodeSystem(system))
                {
                    ValidationErrors.Add(errors, "goodsCodeSystem", $"Code system must be one of: {string.Join(", ", ArticleRules.GoodsCodeSystems)}.");
                }
                else if (!ArticleRules.IsGoodsCodeFor(system, goodsCode))
                {
                    ValidationErrors.Add(errors, "goodsCode", $"A {system} code has exactly {ArticleRules.GoodsCodeLength(system)} digits (leading zeros included).");
                }
            }
            else if (system != null && !ArticleRules.IsGoodsCodeSystem(system))
            {
                ValidationErrors.Add(errors, "goodsCodeSystem", $"Code system must be one of: {string.Join(", ", ArticleRules.GoodsCodeSystems)}.");
            }
            if (goodsCode == null)
            {
                system = null; // een stelsel zonder code wordt niet vastgelegd
            }
            var tradeClass = ValidationErrors.OptionalText(request.TradeClass, ArticleRules.MaxTradeClassLength, "tradeClass", errors);

            // GGN is geen artikelstandaard meer (leveranciers beheren hun GGN's in de relatiewerkplek). Een oude waarde in
            // ProductVariant.DefaultGgn blijft staan en wordt via api/relations/reference gerapporteerd voor handmatige toewijzing.
            return new Values(productName, variantName, shortcut, country?.Code, country?.NameNl, brand, size, packaging, packagingUnit, season, notes, method,
                request.DefaultNetKgPerPackage, request.DefaultGrossKgPerPackage, request.DefaultPackagesPerPallet, goodsCode, system, tradeClass);
        }

        private static void Apply(ProductVariant variant, Values values)
        {
            variant.Name = values.VariantName;
            variant.ShortcutCode = values.ShortcutCode;
            variant.OriginCountryCode = values.OriginCountryCode;
            variant.Origin = values.Origin;
            variant.Brand = values.Brand;
            variant.SizeCategory = values.SizeCategory;
            variant.Packaging = values.Packaging;
            variant.PackagingUnit = values.PackagingUnit;
            variant.Season = values.Season;
            variant.Notes = values.Notes;
            variant.WeightMethod = values.WeightMethod;
            variant.DefaultNetKgPerPackage = values.NetKg;
            variant.DefaultGrossKgPerPackage = values.GrossKg;
            variant.DefaultPackagesPerPallet = values.PackagesPerPallet;
            variant.GoodsCode = values.GoodsCode;
            variant.GoodsCodeSystem = values.GoodsCodeSystem;
            variant.TradeClass = values.TradeClass;
        }

        // Zelfde opbouw als describeVariant in de frontend (lib/productSearch.ts): soort · ras · verpakking · herkomst · maat.
        public static string Describe(ProductVariant variant) =>
            string.Join(" · ", new[] { variant.Product?.Name, variant.Name, variant.Packaging, variant.Origin, variant.SizeCategory }
                .Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));

        public static ArticleDto ToDto(ProductVariant v) => new()
        {
            Id = v.Id,
            ProductId = v.ProductId,
            ProductName = v.Product?.Name,
            VariantName = v.Name ?? "",
            Description = Describe(v),
            ShortcutCode = Blank(v.ShortcutCode),
            Origin = Blank(v.Origin),
            OriginCountryCode = v.OriginCountryCode,
            Brand = Blank(v.Brand),
            SizeCategory = Blank(v.SizeCategory),
            Packaging = Blank(v.Packaging),
            PackagingUnit = Blank(v.PackagingUnit),
            Season = Blank(v.Season),
            Notes = Blank(v.Notes),
            WeightMethod = v.WeightMethod,
            DefaultNetKgPerPackage = v.DefaultNetKgPerPackage,
            DefaultGrossKgPerPackage = v.DefaultGrossKgPerPackage,
            DefaultPackagesPerPallet = v.DefaultPackagesPerPallet,
            GoodsCode = Blank(v.GoodsCode),
            GoodsCodeSystem = Blank(v.GoodsCodeSystem),
            TradeClass = Blank(v.TradeClass),
            DefaultPrice = v.DefaultPrice
        };

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
