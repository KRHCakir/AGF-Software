using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Inkoopfactuur invoeren vanuit de kantoorwerkplek. Puur administratief: een inkoopfactuur wijzigt nooit voorraad,
    // pallets of kwaliteit; ontvangst blijft een aparte, logistieke handeling.
    // Idempotent via requestId: een identieke herhaling geeft dezelfde factuur (200), een afwijkende 409.
    public sealed class PurchaseInvoiceService
    {
        private const int MaxInvoiceNumberLength = 100;
        private const int MaxTextLength = 200;
        private const int MaxRemarksLength = 1000;
        private const int MaxDescriptionLength = 200;

        private readonly AppDbContext _db;

        public PurchaseInvoiceService(AppDbContext db)
        {
            _db = db;
        }

        // ---- Instellingen ----

        public async Task<CompanySettings> SettingsAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _db.CompanySettings.FirstOrDefaultAsync(cancellationToken);
            if (settings == null)
            {
                settings = new CompanySettings();
                _db.CompanySettings.Add(settings);
                await _db.SaveChangesAsync(cancellationToken);
            }
            return settings;
        }

        public static PurchaseInvoiceSettingsDto ToSettingsDto(CompanySettings settings) => new()
        {
            AllOptionalFields = PurchaseInvoiceRules.OptionalFields.ToList(),
            OptionalFields = PurchaseInvoiceRules.OptionalFields.Where(PurchaseInvoiceRules.ParseEnabled(settings.PurchaseInvoiceOptionalFields).Contains).ToList(),
            DefaultVatPercentage = settings.PurchaseInvoiceDefaultVatPercentage,
            DefaultPaymentTermsDays = settings.PurchaseInvoiceDefaultPaymentTermsDays,
            DefaultCurrency = settings.PurchaseInvoiceDefaultCurrency
        };

        public async Task<ServiceResult<PurchaseInvoiceSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            ServiceResult<PurchaseInvoiceSettingsDto>.Ok(ToSettingsDto(await SettingsAsync(cancellationToken)));

        public async Task<ServiceResult<PurchaseInvoiceSettingsDto>> UpdateSettingsAsync(UpdatePurchaseInvoiceSettingsDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return Validation<PurchaseInvoiceSettingsDto>(errors);
            }
            var unknown = (request.OptionalFields ?? new List<string>()).Where(f => !PurchaseInvoiceRules.IsOptionalField(f)).ToList();
            if (unknown.Count > 0)
            {
                ValidationErrors.Add(errors, "optionalFields", $"Unknown optional fields: {string.Join(", ", unknown)}. Allowed: {string.Join(", ", PurchaseInvoiceRules.OptionalFields)}.");
            }
            if (request.DefaultVatPercentage != null && !IsPercentage(request.DefaultVatPercentage.Value))
            {
                ValidationErrors.Add(errors, "defaultVatPercentage", "Default VAT percentage must be between 0 and 100 with at most 2 decimals.");
            }
            if (request.DefaultPaymentTermsDays != null && (request.DefaultPaymentTermsDays < 0 || request.DefaultPaymentTermsDays > PurchaseInvoiceRules.MaxPaymentTermsDays))
            {
                ValidationErrors.Add(errors, "defaultPaymentTermsDays", $"Default payment terms must be between 0 and {PurchaseInvoiceRules.MaxPaymentTermsDays} days.");
            }
            var currency = request.DefaultCurrency?.Trim().ToUpperInvariant();
            if (currency != null && !PurchaseInvoiceRules.IsCurrencyCode(currency))
            {
                ValidationErrors.Add(errors, "defaultCurrency", "Default currency must be a three-letter code (for example EUR).");
            }
            if (errors.Count > 0)
            {
                return Validation<PurchaseInvoiceSettingsDto>(errors);
            }

            var settings = await SettingsAsync(cancellationToken);
            if (request.OptionalFields != null) settings.PurchaseInvoiceOptionalFields = PurchaseInvoiceRules.Serialize(request.OptionalFields);
            if (request.DefaultVatPercentage != null) settings.PurchaseInvoiceDefaultVatPercentage = request.DefaultVatPercentage.Value;
            if (request.DefaultPaymentTermsDays != null) settings.PurchaseInvoiceDefaultPaymentTermsDays = request.DefaultPaymentTermsDays.Value;
            if (currency != null) settings.PurchaseInvoiceDefaultCurrency = currency;
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<PurchaseInvoiceSettingsDto>.Ok(ToSettingsDto(settings));
        }

        // ---- Leveranciers met hun standaardwaarden (voorstellen; het document bewaart de gebruikte waarden) ----

        public async Task<IReadOnlyList<PurchaseVendorDto>> VendorsAsync(CancellationToken cancellationToken = default) =>
            await _db.Vendors.AsNoTracking().OrderBy(v => v.Name)
                .Select(v => new PurchaseVendorDto { Id = v.Id, Name = v.Name, VatNumber = v.VATNumber, PaymentTermsDays = v.PaymentTermsDays, DefaultCurrency = v.DefaultCurrency })
                .ToListAsync(cancellationToken);

        // ---- Aanmaken ----

        public async Task<ServiceResult<PurchaseInvoiceDto>> CreateAsync(CreatePurchaseInvoiceDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return Validation<PurchaseInvoiceDto>(errors);
            }
            if (request.RequestId == null || request.RequestId == Guid.Empty)
            {
                ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }

            var settings = await SettingsAsync(cancellationToken);
            var enabled = PurchaseInvoiceRules.ParseEnabled(settings.PurchaseInvoiceOptionalFields);

            Vendor vendor = null;
            if (request.VendorId == null)
            {
                ValidationErrors.Add(errors, "vendorId", "A supplier is required.");
            }
            else
            {
                vendor = await _db.Vendors.AsNoTracking().SingleOrDefaultAsync(v => v.Id == request.VendorId, cancellationToken);
                if (vendor == null)
                {
                    ValidationErrors.Add(errors, "vendorId", $"Supplier {request.VendorId} does not exist.");
                }
            }

            var invoiceNumber = ValidationErrors.RequiredText(request.InvoiceNumber, MaxInvoiceNumberLength, "invoiceNumber", errors);
            // Kalenderdatums (factuur-, verval- en ontvangstdatum) worden als UTC-middernacht opgeslagen, ongeacht hoe de client
            // ze stuurt ("2026-10-21" komt binnen als Kind=Unspecified; PostgreSQL eist UTC).
            var invoiceDate = AsUtcDate(request.InvoiceDate);
            if (invoiceDate == null)
            {
                ValidationErrors.Add(errors, "invoiceDate", "An invoice date is required.");
            }

            // Betaaltermijn en valuta: op het document bewaard; leeg = leveranciersstandaard, anders bedrijfsstandaard.
            var terms = request.PaymentTermsDays ?? vendor?.PaymentTermsDays ?? settings.PurchaseInvoiceDefaultPaymentTermsDays;
            if (terms < 0 || terms > PurchaseInvoiceRules.MaxPaymentTermsDays)
            {
                ValidationErrors.Add(errors, "paymentTermsDays", $"Payment terms must be between 0 and {PurchaseInvoiceRules.MaxPaymentTermsDays} days.");
            }
            var currency = (request.Currency?.Trim() ?? "").ToUpperInvariant();
            if (currency == "")
            {
                currency = (vendor?.DefaultCurrency ?? "").Trim().ToUpperInvariant();
                if (currency == "") currency = settings.PurchaseInvoiceDefaultCurrency;
            }
            if (!PurchaseInvoiceRules.IsCurrencyCode(currency))
            {
                ValidationErrors.Add(errors, "currency", "Currency must be a three-letter code (for example EUR).");
            }
            var dueDate = AsUtcDate(request.DueDate) ?? invoiceDate?.AddDays(terms);
            if (invoiceDate != null && dueDate < invoiceDate)
            {
                ValidationErrors.Add(errors, "dueDate", "The due date cannot be before the invoice date.");
            }

            // Optionele velden: alleen ingeschakelde velden worden overgenomen; de rest wordt genegeerd (niet geweigerd).
            string Text(string key, string value, int max) => enabled.Contains(key) ? ValidationErrors.OptionalText(value, max, key, errors) : null;
            decimal? Amount(string key, decimal? value)
            {
                if (!enabled.Contains(key) || value == null) return null;
                if (value < 0m || decimal.Round(value.Value, 3) != value.Value || value > SupplyRules.MaxDecimal18_3)
                {
                    ValidationErrors.Add(errors, key, "Must be zero or positive with at most 3 decimals.");
                }
                return value;
            }
            var receivedDate = enabled.Contains("receivedDate") ? AsUtcDate(request.ReceivedDate) : null;
            var paymentReference = Text("paymentReference", request.PaymentReference, MaxTextLength);
            var remarks = Text("remarks", request.Remarks, MaxRemarksLength);
            var grossWeightKg = Amount("grossWeightKg", request.GrossWeightKg);
            var netWeightKg = Amount("netWeightKg", request.NetWeightKg);
            if (grossWeightKg != null && netWeightKg != null && netWeightKg > grossWeightKg)
            {
                ValidationErrors.Add(errors, "netWeightKg", "Net weight cannot exceed gross weight.");
            }
            var containerNumber = Text("containerNumber", request.ContainerNumber, MaxTextLength);
            var transporter = Text("transporter", request.Transporter, MaxTextLength);
            var driverName = Text("driverName", request.DriverName, MaxTextLength);
            var licensePlate = Text("licensePlate", request.LicensePlate, 32);
            var incoterms = Text("incoterms", request.Incoterms, 32);
            var customsValue = Amount("customsValue", request.CustomsValue);
            var originCountry = Text("originCountry", request.OriginCountry, MaxTextLength);
            var receiptReference = Text("receiptReference", request.ReceiptReference, MaxTextLength);

            // Regels: artikel (variant) of vrije omschrijving; aantal, eenheid, prijs, korting en btw expliciet; bedragen berekend.
            var lines = new List<IncomingInvoiceLine>();
            if (request.Lines == null || request.Lines.Count == 0)
            {
                ValidationErrors.Add(errors, "lines", "At least one line is required.");
            }
            else if (request.Lines.Count > PurchaseInvoiceRules.MaxLines)
            {
                ValidationErrors.Add(errors, "lines", $"At most {PurchaseInvoiceRules.MaxLines} lines are allowed.");
            }
            else
            {
                var variantIds = request.Lines.Where(l => l?.ProductVariantId != null).Select(l => l.ProductVariantId.Value).Distinct().ToList();
                var variants = await _db.ProductVariants.AsNoTracking().Include(v => v.Product)
                    .Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
                for (var i = 0; i < request.Lines.Count; i++)
                {
                    var key = $"lines[{i}]";
                    var line = request.Lines[i];
                    if (line == null)
                    {
                        ValidationErrors.Add(errors, key, "A line is required.");
                        continue;
                    }
                    var before = errors.Count;
                    ProductVariant variant = null;
                    if (line.ProductVariantId != null && !variants.TryGetValue(line.ProductVariantId.Value, out variant))
                    {
                        ValidationErrors.Add(errors, $"{key}.productVariantId", $"Product variant {line.ProductVariantId} does not exist.");
                    }
                    var description = string.IsNullOrWhiteSpace(line.Description)
                        ? (variant == null ? null : $"{variant.Product?.Name} · {variant.Name}")
                        : line.Description.Trim();
                    if (description == null)
                    {
                        ValidationErrors.Add(errors, $"{key}.description", "A description or a product variant is required.");
                    }
                    else if (description.Length > MaxDescriptionLength)
                    {
                        ValidationErrors.Add(errors, $"{key}.description", $"This field may contain at most {MaxDescriptionLength} characters.");
                    }
                    if (line.Quantity == null || !SupplyRules.IsPositiveDecimal18_3(line.Quantity.Value))
                    {
                        ValidationErrors.Add(errors, $"{key}.quantity", "Quantity is required, must be positive and may have at most 3 decimals.");
                    }
                    var unit = string.IsNullOrWhiteSpace(line.Unit) ? null : line.Unit.Trim();
                    if (unit != null && !SupplyRules.IsPackageUnit(unit))
                    {
                        ValidationErrors.Add(errors, $"{key}.unit", $"Unit must be empty or one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
                    }
                    if (line.UnitPrice == null || line.UnitPrice < 0m || decimal.Round(line.UnitPrice.Value, 2) != line.UnitPrice.Value || line.UnitPrice > 999_999_999_999m)
                    {
                        ValidationErrors.Add(errors, $"{key}.unitPrice", "Unit price is required, must be zero or positive and may have at most 2 decimals.");
                    }
                    var discount = line.DiscountPercentage ?? 0m;
                    if (!IsPercentage(discount))
                    {
                        ValidationErrors.Add(errors, $"{key}.discountPercentage", "Discount must be between 0 and 100 with at most 2 decimals.");
                    }
                    var vat = line.VatPercentage ?? settings.PurchaseInvoiceDefaultVatPercentage;
                    if (!IsPercentage(vat))
                    {
                        ValidationErrors.Add(errors, $"{key}.vatPercentage", "VAT percentage must be between 0 and 100 with at most 2 decimals.");
                    }
                    if (errors.Count > before)
                    {
                        continue;
                    }
                    var net = InvoiceMath.LineNet(line.Quantity!.Value, line.UnitPrice!.Value, discount);
                    lines.Add(new IncomingInvoiceLine
                    {
                        ProductVariantId = line.ProductVariantId,
                        Description = description,
                        Quantity = line.Quantity.Value,
                        Unit = unit,
                        UnitPrice = line.UnitPrice.Value,
                        DiscountPercentage = discount,
                        LineTotal = net,
                        VATPercentage = vat,
                        VATAmount = InvoiceMath.LineVat(net, vat)
                    });
                }
            }

            if (errors.Count > 0)
            {
                return Validation<PurchaseInvoiceDto>(errors);
            }

            var totals = InvoiceMath.Totals(lines.Select(l => (l.LineTotal, l.VATAmount)));
            var candidate = new IncomingInvoice
            {
                RequestId = request.RequestId,
                VendorId = vendor.Id,
                InvoiceNumber = invoiceNumber,
                InvoiceDate = invoiceDate,
                PaymentTermsDays = terms,
                DueDate = dueDate,
                Currency = currency,
                Status = "Draft",
                ReviewConfidence = 1.0,
                TotalAmount = totals.Gross,
                VATAmount = totals.Vat,
                ReceivedDate = receivedDate,
                PaymentReference = paymentReference,
                Remarks = remarks,
                GrossWeightKg = grossWeightKg,
                NetWeightKg = netWeightKg,
                ContainerNumber = containerNumber,
                Transporter = transporter,
                DriverName = driverName,
                LicensePlate = licensePlate,
                Incoterms = incoterms,
                CustomsValue = customsValue,
                OriginCountry = originCountry,
                ReceiptReference = receiptReference,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                InvoiceLines = lines
            };

            // Idempotentie: dezelfde requestId geeft dezelfde factuur terug, een afwijkende inhoud een conflict.
            var existing = await Query().SingleOrDefaultAsync(i => i.RequestId == candidate.RequestId, cancellationToken);
            if (existing != null)
            {
                return ResultForRetry(existing, candidate);
            }

            _db.IncomingInvoices.Add(candidate);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                var concurrent = await Query().SingleOrDefaultAsync(i => i.RequestId == candidate.RequestId, cancellationToken);
                if (concurrent != null)
                {
                    return ResultForRetry(concurrent, candidate);
                }
                throw;
            }

            return ServiceResult<PurchaseInvoiceDto>.Created(ToDto(await Query().SingleAsync(i => i.Id == candidate.Id, cancellationToken)));
        }

        private static ServiceResult<PurchaseInvoiceDto> ResultForRetry(IncomingInvoice existing, IncomingInvoice candidate)
        {
            var existingLines = existing.InvoiceLines.OrderBy(l => l.Id).ToList();
            var candidateLines = candidate.InvoiceLines.ToList();
            var same = existing.VendorId == candidate.VendorId
                && string.Equals(existing.InvoiceNumber, candidate.InvoiceNumber, StringComparison.Ordinal)
                && existing.InvoiceDate == candidate.InvoiceDate
                && existing.PaymentTermsDays == candidate.PaymentTermsDays
                && existing.DueDate == candidate.DueDate
                && string.Equals(existing.Currency, candidate.Currency, StringComparison.Ordinal)
                && existing.TotalAmount == candidate.TotalAmount
                && existing.VATAmount == candidate.VATAmount
                && existingLines.Count == candidateLines.Count
                && existingLines.Zip(candidateLines).All(pair =>
                    pair.First.ProductVariantId == pair.Second.ProductVariantId
                    && string.Equals(pair.First.Description, pair.Second.Description, StringComparison.Ordinal)
                    && pair.First.Quantity == pair.Second.Quantity
                    && string.Equals(pair.First.Unit, pair.Second.Unit, StringComparison.Ordinal)
                    && pair.First.UnitPrice == pair.Second.UnitPrice
                    && pair.First.DiscountPercentage == pair.Second.DiscountPercentage
                    && pair.First.VATPercentage == pair.Second.VATPercentage);
            return same
                ? ServiceResult<PurchaseInvoiceDto>.Ok(ToDto(existing))
                : ServiceResult<PurchaseInvoiceDto>.Conflict($"RequestId {candidate.RequestId} was already used for a different purchase invoice.");
        }

        // ---- Lezen ----

        public async Task<ServiceResult<PurchaseInvoiceDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var invoice = await Query().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
            return invoice == null
                ? ServiceResult<PurchaseInvoiceDto>.NotFound($"Purchase invoice {id} does not exist.")
                : ServiceResult<PurchaseInvoiceDto>.Ok(ToDto(invoice));
        }

        public async Task<IReadOnlyList<PurchaseInvoiceDto>> ListAsync(CancellationToken cancellationToken = default) =>
            (await Query().OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync(cancellationToken)).Select(ToDto).ToList();

        private IQueryable<IncomingInvoice> Query() =>
            _db.IncomingInvoices.AsNoTracking().Include(i => i.Vendor).Include(i => i.InvoiceLines);

        // Alleen de kalenderdag telt; Kind wordt UTC zodat de waarde door Npgsql wordt geaccepteerd en overal gelijk is.
        private static DateTime? AsUtcDate(DateTime? value) =>
            value == null ? null : DateTime.SpecifyKind(value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime().Date : value.Value.Date, DateTimeKind.Utc);

        private static bool IsPercentage(decimal value) => value >= 0m && value <= 100m && decimal.Round(value, 2) == value;

        private static ServiceResult<T> Validation<T>(Dictionary<string, List<string>> errors) =>
            ServiceResult<T>.Validation(ValidationErrors.ToArrays(errors));

        public static PurchaseInvoiceDto ToDto(IncomingInvoice invoice) => new()
        {
            Id = invoice.Id,
            RequestId = invoice.RequestId,
            VendorId = invoice.VendorId,
            VendorName = invoice.Vendor?.Name,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            PaymentTermsDays = invoice.PaymentTermsDays,
            DueDate = invoice.DueDate,
            Currency = invoice.Currency,
            Status = invoice.Status,
            SubTotal = invoice.TotalAmount - invoice.VATAmount,
            VatAmount = invoice.VATAmount,
            TotalAmount = invoice.TotalAmount,
            CreatedAt = invoice.CreatedAt,
            ReceivedDate = invoice.ReceivedDate,
            PaymentReference = invoice.PaymentReference,
            Remarks = invoice.Remarks,
            GrossWeightKg = invoice.GrossWeightKg,
            NetWeightKg = invoice.NetWeightKg,
            ContainerNumber = invoice.ContainerNumber,
            Transporter = invoice.Transporter,
            DriverName = invoice.DriverName,
            LicensePlate = invoice.LicensePlate,
            Incoterms = invoice.Incoterms,
            CustomsValue = invoice.CustomsValue,
            OriginCountry = invoice.OriginCountry,
            ReceiptReference = invoice.ReceiptReference,
            Lines = (invoice.InvoiceLines ?? new List<IncomingInvoiceLine>()).OrderBy(l => l.Id).Select(l => new PurchaseInvoiceLineDto
            {
                Id = l.Id,
                ProductVariantId = l.ProductVariantId,
                Description = l.Description,
                Quantity = l.Quantity,
                Unit = l.Unit,
                UnitPrice = l.UnitPrice,
                DiscountPercentage = l.DiscountPercentage,
                LineTotal = l.LineTotal,
                VatPercentage = l.VATPercentage,
                VatAmount = l.VATAmount
            }).ToList()
        };
    }
}
