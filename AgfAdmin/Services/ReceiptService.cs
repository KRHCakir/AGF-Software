using System.Security.Cryptography;
using System.Text;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Feedback 22 november: ontvangen in één scherm, met één klik per regel. Geen controlevragen meer bij ontvangst (ander product, andere
    // maat of herkomst): komt er iets anders, dan past de gebruiker de orderregel aan (PurchaseOrderService.Edit, splitsen). De ontvangen
    // pallets en regels maakt de bestaande PalletService, met dezelfde regels, audit en idempotentie: deze service kiest alleen de waarden
    // uit de orderregel (partij, eenheid, kg per verpakking, GGN) en verdeelt het aantal over de pallets. De voorraad start, zoals altijd, in
    // Quarantine; keuren of afwijzen blijft de bestaande keuring (optioneel, ook later).
    public sealed class ReceiptService
    {
        public const int MaxPallets = 99;
        public const int MaxReferenceLength = 64;

        private readonly AppDbContext _db;
        private readonly ICurrentUser _user;

        public ReceiptService(AppDbContext db, ICurrentUser currentUser = null)
        {
            _db = db;
            _user = currentUser ?? CurrentUser.Default;
        }

        // ---- Dit wordt verwacht ----

        public async Task<ServiceResult<ReceiptOrderDto>> GetAsync(int orderId, CancellationToken cancellationToken = default)
        {
            var order = await _db.SupplyCommitments.AsNoTracking()
                .Include(c => c.Vendor).Include(c => c.Partij).Include(c => c.ReceiveLocation)
                .Include(c => c.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product)
                .Include(c => c.Lines).ThenInclude(l => l.Partij)
                .AsSplitQuery()
                .SingleOrDefaultAsync(c => c.Id == orderId, cancellationToken);
            if (order == null)
            {
                return ServiceResult<ReceiptOrderDto>.NotFound($"Purchase order {orderId} does not exist.");
            }

            var lineIds = order.Lines.Select(l => l.Id).ToList();
            // Fysieke ontvangst zoals de inkooporder (PurchaseOrderService.LoadFacts): geen teruggeboekte of afgesplitste inhoud, geen
            // geannuleerde pallet, alleen in de eenheid van de regel.
            var receipts = await _db.PalletContentLines.AsNoTracking()
                .Where(c => lineIds.Contains(c.SupplyCommitmentLineId) && c.OriginCorrectionLineId == null && c.OriginRejectionLineId == null
                    && c.Pallet.LogisticsStatus != PalletRules.StatusCancelled)
                .OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.SupplyCommitmentLineId, c.Quantity, c.Unit, c.NetWeightKg, c.QualityStatus, c.PalletId, c.Pallet.Reference })
                .ToListAsync(cancellationToken);

            var lines = order.Lines.OrderBy(l => l.Id).Select(line =>
            {
                var own = receipts.Where(r => r.SupplyCommitmentLineId == line.Id).ToList();
                var received = own.Where(r => line.ExpectedUnit == null || string.Equals(r.Unit, line.ExpectedUnit, StringComparison.Ordinal)).Sum(r => r.Quantity);
                return new ReceiptLineDto
                {
                    Id = line.Id,
                    ProductId = line.ProductVariant.ProductId,
                    ProductName = line.ProductVariant.Product?.Name,
                    ProductVariantName = line.ProductVariant.Name,
                    Origin = line.ProductVariant.Origin,
                    Size = line.ProductVariant.SizeCategory,
                    PartijNumber = line.Partij?.Number,
                    Unit = line.ExpectedUnit,
                    OrderedQuantity = line.ExpectedQuantity,
                    ReceivedQuantity = received,
                    OpenQuantity = Math.Max(0m, line.ExpectedQuantity - received),
                    NetKgPerPackage = KgPerPackage(line),
                    SuggestedPalletCount = line.PalletCount is decimal count && count >= 1m && count <= MaxPallets && count == decimal.Truncate(count) ? (int)count : null,
                    Receivable = line.ExpectedUnit != null,
                    Pallets = own.Select(r => new ReceiptPalletDto
                    {
                        PalletId = r.PalletId, Reference = r.Reference, ContentLineId = r.Id, Quantity = r.Quantity, NetWeightKg = r.NetWeightKg, QualityStatus = r.QualityStatus,
                    }).ToList(),
                };
            }).ToList();

            var dto = new ReceiptOrderDto
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                SupplierReference = order.SupplierReference,
                VendorId = order.VendorId,
                VendorName = order.Vendor?.Name,
                ExpectedAt = order.ExpectedAt == null ? null : DateTime.SpecifyKind(order.ExpectedAt.Value, DateTimeKind.Utc),
                PartijNumber = order.Partij?.Number,
                LocationId = order.ReceiveLocationId,
                LocationName = order.ReceiveLocation?.Name,
                LocationKind = order.ReceiveLocation?.Kind ?? StockLocationRules.KindOwnWarehouse,
                Status = PurchaseOrderRules.OrderStatus(order.Status, lines.Select(l => (l.OrderedQuantity, l.ReceivedQuantity))),
                NotReceivableReason = NotReceivable(order),
                Lines = lines,
            };
            dto.Receivable = dto.NotReceivableReason == null;
            if (dto.Receivable)
            {
                var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
                dto.CertificateWarnings = await CertificateCheck.CheckAsync(_db, order.VendorId, productIds, _db.BusinessCalendar.Today(), cancellationToken);
                dto.CertificateWarningToken = WarningToken(dto.CertificateWarnings);
            }
            return ServiceResult<ReceiptOrderDto>.Ok(dto);
        }

        // ---- Ontvangen ----

        public async Task<ServiceResult<ReceiveLineResultDto>> ReceiveLineAsync(int orderId, int lineId, ReceiveLineDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null)
            {
                Add(errors, "request", "A request body is required.");
                return Validation(errors);
            }
            if (request.RequestId == null || request.RequestId == Guid.Empty)
            {
                Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
            if (request.Quantity == null || !SupplyRules.IsPositiveDecimal18_3(request.Quantity.Value))
            {
                Add(errors, "quantity", "The received quantity is required, must be positive and may have at most 3 decimals.");
            }
            var palletCount = request.PalletCount ?? Math.Max(1, request.PalletReferences?.Count ?? 0);
            if (palletCount < 1 || palletCount > MaxPallets)
            {
                Add(errors, "palletCount", $"The number of pallets must be between 1 and {MaxPallets}.");
            }
            var references = (request.PalletReferences ?? new List<string>()).Select(r => string.IsNullOrWhiteSpace(r) ? null : r.Trim()).ToList();
            if (references.Count > palletCount)
            {
                Add(errors, "palletReferences", "There are more pallet numbers than pallets.");
            }
            for (var i = 0; i < references.Count; i++)
            {
                if (references[i] != null && references[i].Length > MaxReferenceLength)
                {
                    Add(errors, $"palletReferences[{i}]", $"A pallet number may contain at most {MaxReferenceLength} characters.");
                }
            }
            if (references.Where(r => r != null).GroupBy(r => r, StringComparer.Ordinal).Any(g => g.Count() > 1))
            {
                Add(errors, "palletReferences", "Each pallet number may occur only once.");
            }
            if (errors.Count > 0) return Validation(errors);

            var order = await _db.SupplyCommitments.AsNoTracking().Include(c => c.Lines).ThenInclude(l => l.ProductVariant)
                .SingleOrDefaultAsync(c => c.Id == orderId, cancellationToken);
            if (order == null) return ServiceResult<ReceiveLineResultDto>.NotFound($"Purchase order {orderId} does not exist.");
            var line = order.Lines.SingleOrDefault(l => l.Id == lineId);
            if (line == null) return ServiceResult<ReceiveLineResultDto>.NotFound($"Line {lineId} does not belong to purchase order {orderId}.");
            var refused = NotReceivable(order);
            if (refused != null) return ServiceResult<ReceiveLineResultDto>.Conflict(refused);
            if (line.ExpectedUnit == null)
            {
                return ServiceResult<ReceiveLineResultDto>.Conflict("This order line has no unit; add the unit on the order line first.");
            }

            var quantity = request.Quantity!.Value;
            var shares = Split(quantity, palletCount);
            if (shares == null)
            {
                Add(errors, "palletCount", "Each pallet needs at least one package; choose fewer pallets.");
                return Validation(errors);
            }

            // Gewicht: uit de order (kg per verpakking van de regel, anders van het artikel), zodat er geen gewichtsvraag komt. Alleen als de
            // order geen kg kent, vult de ontvanger het netto kg in.
            var kgPerPackage = KgPerPackage(line);
            if (kgPerPackage != null && request.NetWeightKg != null)
            {
                Add(errors, "netWeightKg", "The weight follows from the order line; change the order line if something else arrived.");
            }
            if (kgPerPackage == null && (request.NetWeightKg == null || !SupplyRules.IsPositiveDecimal18_3(request.NetWeightKg.Value)))
            {
                Add(errors, "netWeightKg", "The order line has no kg per package: enter the net kg received (positive, at most 3 decimals).");
            }
            if (errors.Count > 0) return Validation(errors);
            var weights = WeightsFor(shares, kgPerPackage, request.NetWeightKg);
            if (weights == null)
            {
                Add(errors, "netWeightKg", "The net kg is too small to divide over these pallets.");
                return Validation(errors);
            }

            // Lotnummer: de partij van de regel neemt PalletService over. Zonder partij krijgt de order er bij deze ontvangst een (regel 41):
            // met de nummering aan het volgende voorstel (I-…); met eigen nummers het lot van de regel, anders dat van de leverancier, anders
            // het ordernummer, zodat de ontvangst altijd herleidbaar is tot deze order.
            var ownNumbers = (await PartijService.SettingsAsync(_db, cancellationToken)).PartijNumberMode != PartijRules.ModeAuto;
            var lot = line.PartijId != null || order.PartijId != null || !ownNumbers ? null : FirstText(line.LotNumber, order.SupplierLotReference, order.OrderNumber);
            var ggn = line.Ggn ?? await ProposedGgnAsync(order.VendorId, cancellationToken);
            var requestId = request.RequestId!.Value;

            // De GGN-melding moet de ontvanger echt gezien hebben: het kenmerk uit het scherm moet bij de actuele meldingen passen. Zonder
            // (of met een verouderd) kenmerk komt eerst de actuele melding (409), en wordt er niets vastgelegd.
            var orderWarnings = await CertificateCheck.CheckAsync(_db, order.VendorId, order.Lines.Select(l => l.ProductVariant.ProductId).Distinct().ToList(), _db.BusinessCalendar.Today(), cancellationToken);
            var lineWarnings = orderWarnings.Where(w => w.ProductId == line.ProductVariant.ProductId).ToList();
            if (lineWarnings.Count > 0 && !string.Equals(request.CertificateWarningToken, WarningToken(orderWarnings), StringComparison.Ordinal))
            {
                return ServiceResult<ReceiveLineResultDto>.Conflict(CertificateCheck.ConflictMessage(lineWarnings), CertificateCheck.ConflictBody(lineWarnings));
            }

            // Het RequestId hoort bij de hele aanvraag: een eerdere ontvangst is atomair vastgelegd, dus bestaan er pallets onder dit RequestId,
            // dan moet de herhaling precies evenveel pallets vragen. Anders (andere verdeling of aantal) wordt niets bijgemaakt.
            var derivedKeys = Enumerable.Range(0, MaxPallets).Select(i => (Guid?)Derived(requestId, $"pallet:{i}")).ToList();
            var existingPallets = await _db.Pallets.AsNoTracking().CountAsync(p => derivedKeys.Contains(p.RequestId), cancellationToken);
            if (existingPallets > 0 && existingPallets != shares.Count)
            {
                return ServiceResult<ReceiveLineResultDto>.Conflict($"RequestId {requestId} was already used for a receipt with {existingPallets} pallet(s); use a new requestId for different input.");
            }

            // Alles of niets: alle pallets en regels in één transactie; bij een fout wordt niets opgeslagen, zodat een herhaling niets dubbel ontvangt.
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var pallets = new PalletService(_db, _user);
            var created = new List<ReceiptPalletDto>();
            for (var i = 0; i < shares.Count; i++)
            {
                var pallet = await pallets.CreateAsync(new CreatePalletDto
                {
                    RequestId = Derived(requestId, $"pallet:{i}"),
                    Reference = i < references.Count ? references[i] : null,
                    // Ontvangen in onze voorraad (ook onderweg: verkocht en geleverd zoals elke andere voorraad); waar de pallet fysiek is, zegt
                    // de voorraadlocatie van de order (null = de eigen loods).
                    LogisticsStatus = PalletRules.StatusReceived,
                    LocationContext = PalletRules.LocationWarehouse,
                    StockLocationId = order.ReceiveLocationId,
                }, requireActiveLocation: false, cancellationToken);
                if (pallet.Status is not (ServiceResultStatus.Created or ServiceResultStatus.Ok)) return Relay(pallet.Status, pallet.Message, pallet.Errors, pallet.ConflictBody, $"pallets[{i}]");

                var content = await pallets.AddContentLineAsync(pallet.Value.Id, new AddPalletContentLineDto
                {
                    RequestId = Derived(requestId, $"content:{i}"),
                    SupplyCommitmentLineId = line.Id,
                    LotNumber = lot,
                    Quantity = shares[i],
                    Unit = line.ExpectedUnit,
                    WeightMethod = weights.Value.Method,
                    NetKgPerPackage = weights.Value.Method == WeightRules.Calculated ? kgPerPackage : null,
                    NetWeightKg = weights.Value.Kg[i],
                    Ggn = ggn,
                    // Alleen met het kenmerk van de getoonde melding (hierboven gecontroleerd) legt ontvangen vast wie hem zag (CurrentUser).
                    ConfirmCertificate = lineWarnings.Count > 0 ? true : null,
                }, cancellationToken);
                if (content.Status is not (ServiceResultStatus.Created or ServiceResultStatus.Ok)) return Relay(content.Status, content.Message, content.Errors, content.ConflictBody, $"pallets[{i}]");
                created.Add(new ReceiptPalletDto
                {
                    PalletId = pallet.Value.Id, Reference = pallet.Value.Reference, ContentLineId = content.Value.Id, Quantity = content.Value.Quantity,
                    NetWeightKg = content.Value.NetWeightKg, QualityStatus = content.Value.QualityStatus,
                });
            }
            await transaction.CommitAsync(cancellationToken);

            _db.ChangeTracker.Clear();
            var view = await GetAsync(orderId, cancellationToken);
            return ServiceResult<ReceiveLineResultDto>.Created(new ReceiveLineResultDto
            {
                LineId = line.Id, Quantity = quantity, NetWeightKg = weights.Value.Kg.Sum(), Pallets = created, Order = view.Value,
            });
        }

        // ---- Regels (los te toetsen) ----

        // Kg per verpakking zoals de order hem verwacht: netto kg van de regel gedeeld door het aantal, anders de standaard van het artikel.
        public static decimal? KgPerPackage(SupplyCommitmentLine line) =>
            ReceiptDeviationRules.ExpectedKgPerPackage(line.ExpectedQuantity, line.ExpectedNetWeightKg, line.ProductVariant?.DefaultNetKgPerPackage);

        // Verdeelt het aantal over de pallets: hele verpakkingen zo gelijk mogelijk (de eerste pallets één meer), een gebroken rest op de
        // laatste pallet. Null als er een pallet zonder hele verpakking zou overblijven.
        public static List<decimal> Split(decimal quantity, int pallets)
        {
            if (pallets == 1) return new List<decimal> { quantity };
            var whole = decimal.Truncate(quantity);
            var fraction = quantity - whole;
            var each = decimal.Truncate(whole / pallets);
            if (each < 1m) return null;
            var extra = (int)(whole - each * pallets);
            var shares = Enumerable.Range(0, pallets).Select(i => each + (i < extra ? 1m : 0m)).ToList();
            shares[^1] += fraction;
            return shares;
        }

        // Berekend (aantal × kg per verpakking) als de kg per verpakking in drie decimalen past; anders gewogen met het totaal uit de order,
        // of het opgegeven netto kg, naar verhouding verdeeld (afgerond op drie decimalen, de rest op de laatste pallet).
        public static (string Method, List<decimal> Kg)? WeightsFor(IReadOnlyList<decimal> shares, decimal? kgPerPackage, decimal? enteredKg)
        {
            if (kgPerPackage is decimal perPackage && perPackage == decimal.Round(perPackage, 3))
            {
                var calculated = new List<decimal>();
                foreach (var share in shares)
                {
                    if (!WeightRules.TryCalculatedTotal(share, perPackage, out var total)) return null;
                    calculated.Add(total);
                }
                return (WeightRules.Calculated, calculated);
            }
            var quantity = shares.Sum();
            var totalKg = enteredKg ?? decimal.Round(quantity * kgPerPackage!.Value, 3, MidpointRounding.AwayFromZero);
            var kg = shares.Select(share => decimal.Round(totalKg * share / quantity, 3, MidpointRounding.AwayFromZero)).ToList();
            kg[^1] = totalKg - kg.Take(kg.Count - 1).Sum();
            return kg.All(k => k > 0m) ? (WeightRules.Weighed, kg) : null;
        }

        // Een vaste afgeleide sleutel per pallet en inhoudsregel: dezelfde aanvraag (RequestId) geeft bij een herhaling dezelfde pallets en
        // regels terug in plaats van nieuwe; na een gedeeltelijke fout gaat een herhaling verder waar hij stopte.
        public static Guid Derived(Guid requestId, string part)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{requestId:N}/{part}"));
            var bytes = hash.AsSpan(0, 16).ToArray();
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes);
        }

        // Een vast kenmerk van de meldingen (product, soort, certificaten, tekst): verandert er iets aan de melding, dan klopt het kenmerk niet meer.
        public static string WarningToken(IReadOnlyCollection<CertificateCheckDto> warnings)
        {
            if (warnings == null || warnings.Count == 0) return null;
            var text = string.Join("|", warnings.OrderBy(w => w.ProductId).Select(w =>
                $"{w.ProductId}:{string.Join(",", w.Kinds)}:{string.Join(",", w.Certificates.Select(c => c.CertificateId))}:{w.Message}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
        }

        private static string NotReceivable(SupplyCommitment order)
        {
            if (order.Route != SupplyRules.RouteWarehouse) return "This order is customer-bound direct trade; it is not received in a warehouse.";
            if (order.Status == SupplyRules.StatusCancelled) return "This purchase order is cancelled.";
            if (order.Status == SupplyRules.StatusRejected) return "This purchase order is rejected; record a new purchase order for a replacement delivery.";
            return null;
        }

        // Het GGN zoals de oude ontvangst het voorstelde: alleen bij precies één actief GGN van de leverancier.
        private async Task<string> ProposedGgnAsync(int vendorId, CancellationToken cancellationToken)
        {
            var vendor = await _db.Vendors.AsNoTracking().SingleAsync(v => v.Id == vendorId, cancellationToken);
            var options = await CertificateService.GgnOptionsAsync(_db, new[] { vendor }, cancellationToken);
            var list = options.GetValueOrDefault(vendorId) ?? new List<VendorGgnOptionDto>();
            return list.Count == 1 ? list[0].Ggn : null;
        }

        private static string FirstText(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private static ServiceResult<ReceiveLineResultDto> Relay(ServiceResultStatus status, string message, IReadOnlyDictionary<string, string[]> errors, object body, string prefix) => status switch
        {
            ServiceResultStatus.Validation => ServiceResult<ReceiveLineResultDto>.Validation(errors.ToDictionary(e => $"{prefix}.{e.Key}", e => e.Value)),
            ServiceResultStatus.NotFound => ServiceResult<ReceiveLineResultDto>.NotFound(message),
            _ => body == null ? ServiceResult<ReceiveLineResultDto>.Conflict(message) : ServiceResult<ReceiveLineResultDto>.Conflict(message, body),
        };

        private static ServiceResult<ReceiveLineResultDto> Validation(Dictionary<string, List<string>> errors) =>
            ServiceResult<ReceiveLineResultDto>.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        private static void Add(Dictionary<string, List<string>> errors, string key, string message)
        {
            if (!errors.TryGetValue(key, out var list)) errors[key] = list = new List<string>();
            list.Add(message);
        }
    }
}
