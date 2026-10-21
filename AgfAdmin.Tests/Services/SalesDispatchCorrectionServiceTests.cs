using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Services;

// Administratieve verzendcorrectie: compenserende grootboekregels, nieuwe inhoud in quarantaine, Corrected als eindtoestand,
// heropende klantvraag zonder herstelde reserveringen, idempotentie, geschiktheid en races.
public class SalesDispatchCorrectionServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;
    private int _colliLine;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal boxQuantity = 25m, decimal colliQuantity = 10m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity, colliQuantity);
    }

    private async Task<StockReservationDto> ReservedAsync(int contentLineId, decimal quantity, decimal kg, int? lineId = null, string unit = "box")
    {
        await using var db = _factory.CreateContext();
        var result = await new StockReservationService(db).ReserveAsync(_orderId, lineId ?? _boxLine, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentLineId, Quantity = quantity, Unit = unit, NetWeightKg = kg, ActorReference = "office-1"
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    private async Task<SalesDispatchDto> DispatchedAsync(params StockReservationDto[] reservations)
    {
        await using var db = _factory.CreateContext();
        var order = (await new SalesOrderService(db).GetAsync(_orderId)).Value;
        var result = await new SalesDispatchService(db).DispatchAsync(_orderId, new CreateSalesDispatchDto
        {
            RequestId = Guid.NewGuid(), ExpectedOrderVersion = order.Version, ActorReference = "office-1", Reference = "VRB-1",
            Lines = reservations.Select(r => new CreateSalesDispatchLineDto { StockReservationId = r.Id, ExpectedReservationVersion = r.Version }).ToList()
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    private async Task<int> CalculatedApprovedLineAsync(decimal quantity = 10m, decimal kgPerPackage = 4m)
    {
        await using var db = _factory.CreateContext();
        var source = await db.SupplyCommitmentLines.SingleAsync(l => l.SupplyCommitment.Route == "Warehouse" && l.ExpectedUnit == "box");
        var line = new PalletContentLine
        {
            RequestId = Guid.NewGuid(), PalletId = _seed.SecondWarehousePalletId, SupplyCommitmentLineId = source.Id, LotNumber = "LOT-CALC",
            Quantity = quantity, Unit = "box", NetWeightKg = WeightRules.CalculatedTotal(quantity, kgPerPackage),
            WeightMethod = WeightRules.Calculated, NetKgPerPackage = kgPerPackage, QualityStatus = "Approved"
        };
        db.PalletContentLines.Add(line);
        await db.SaveChangesAsync();
        return line.Id;
    }

    // Regressie (review #40): ook verzending en correctie van afgeronde deelreserveringen moeten slagen; de teruggeboekte
    // regels dragen de exact verzonden kg (1,502 en 1,501), niet een herberekend totaal.
    [Fact]
    public async Task Dispatch_And_Correction_Of_Rounded_Partial_Reservations_Keep_The_Exact_Kg()
    {
        await ArrangeAsync();
        // 5 × 1,001 = 5,005 kg; drie deelreserveringen van 1,5 (elk 1,502) laten 0,5 box met 0,499 kg over, terwijl
        // 0,5 × 1,001 = 0,5005: de teruggeboekte restregel wijkt 0,0015 af van de rekensom en moet toch kunnen bestaan.
        var lineId = await CalculatedApprovedLineAsync(5m, 1.001m);
        var a = await ReservedAsync(lineId, 1.5m, 1.502m);
        var b = await ReservedAsync(lineId, 1.5m, 1.502m);
        var c = await ReservedAsync(lineId, 1.5m, 1.502m);
        var remainder = await ReservedAsync(lineId, 0.5m, 0.499m);
        var dispatch = await DispatchedAsync(a, b, c, remainder);

        var result = await CorrectAsync(dispatch.Id, Correct(await OrderAsync(), dispatch));
        Assert.Equal(ServiceResultStatus.Created, result.Status);

        var state = await StateAsync(lineId);
        Assert.Equal(4, state.Restored.Count);
        Assert.Equal(new[] { 0.499m, 1.502m, 1.502m, 1.502m }, state.Restored.Select(r => r.NetWeightKg).OrderBy(x => x).ToArray());
        Assert.All(state.Restored, r => { Assert.Equal(WeightRules.Calculated, r.WeightMethod); Assert.Equal(1.001m, r.NetKgPerPackage); });
        Assert.Equal(5.005m, state.Original.DispatchedNetWeightKg);
    }

    [Fact]
    public async Task Restored_Content_Keeps_The_Calculated_Weight_Method_Of_The_Original()
    {
        await ArrangeAsync();
        var lineId = await CalculatedApprovedLineAsync();
        var reservation = await ReservedAsync(lineId, 5m, 20m);
        var dispatch = await DispatchedAsync(reservation);

        var result = await CorrectAsync(dispatch.Id, Correct(await OrderAsync(), dispatch));
        Assert.Equal(ServiceResultStatus.Created, result.Status);

        var state = await StateAsync(lineId);
        var restored = Assert.Single(state.Restored);
        Assert.Equal(WeightRules.Calculated, restored.WeightMethod);
        Assert.Equal(4m, restored.NetKgPerPackage);
        Assert.Equal(20m, restored.NetWeightKg);
        Assert.Equal(5m, restored.Quantity);
        Assert.Equal(WeightRules.Calculated, state.Original.WeightMethod);
    }

    private async Task<SalesOrderDto> OrderAsync()
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(_orderId)).Value;
    }

    private static CorrectSalesDispatchDto Correct(SalesOrderDto order, SalesDispatchDto dispatch, string reason = "Verkeerde order gescand", bool? confirmed = true) => new()
    {
        RequestId = Guid.NewGuid(),
        ExpectedOrderVersion = order.Version,
        ActorReference = "office-1",
        Reason = reason,
        GoodsNeverLeftWarehouse = confirmed,
        Lines = dispatch.Lines.Select(l => new CreateSalesDispatchLineDto
        {
            StockReservationId = l.StockReservationId,
            ExpectedReservationVersion = order.Lines.SelectMany(x => x.Reservations).Single(r => r.Id == l.StockReservationId).Version
        }).ToList()
    };

    private async Task<ServiceResult<SalesDispatchCorrectionDto>> CorrectAsync(int dispatchId, CorrectSalesDispatchDto request, int? orderId = null, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new SalesDispatchCorrectionService(db).CorrectAsync(orderId ?? _orderId, dispatchId, request);
    }

    private async Task<(PalletContentLine Original, List<PalletContentLine> Restored, List<StockMovement> Movements, List<StockReservation> Reservations, int Corrections, List<PalletAuditEvent> PalletEvents, List<SalesOrderAuditEvent> OrderEvents)> StateAsync(int originalContentId)
    {
        await using var db = _factory.CreateContext();
        return (
            await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == originalContentId),
            await db.PalletContentLines.AsNoTracking().Where(c => c.OriginCorrectionLineId != null).OrderBy(c => c.Id).ToListAsync(),
            await db.StockMovements.AsNoTracking().OrderBy(m => m.Id).ToListAsync(),
            await db.StockReservations.AsNoTracking().Where(r => r.SalesOrderLineId != null).OrderBy(r => r.Id).ToListAsync(),
            await db.SalesDispatchCorrections.CountAsync(),
            await db.PalletAuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync(),
            await db.SalesOrderAuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync());
    }

    // ======================== Voorbeeld uit het plan ========================

    [Fact]
    public async Task Correction_Restores_Quarantine_Content_Keeps_Original_Dispatch_Intact_And_Reopens_Demand()
    {
        await ArrangeAsync(boxQuantity: 25m);
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var r2 = await ReservedAsync(_seed.ApprovedBox, 15m, 60m);
        var d1 = await DispatchedAsync(r1);
        var d2 = await DispatchedAsync(r2);
        var before = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((25m, 100m), (before.Original.DispatchedQuantity, before.Original.DispatchedNetWeightKg));
        var order = await OrderAsync();
        Assert.True(order.Lines.Single(l => l.Id == _boxLine).Fulfilled); // de colli-regel van de gezaaide order blijft open
        var request = Correct(order, d2, reason: "  Verkeerde order gescand ");

        var result = await CorrectAsync(d2.Id, request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var dto = result.Value;
        Assert.Equal((d2.Id, _orderId, "Verkeerde order gescand", true, 1), (dto.SalesDispatchId, dto.SalesOrderId, dto.Reason, dto.GoodsNeverLeftWarehouseConfirmed, dto.Lines.Count));
        Assert.Equal((15m, "box", 60m, "Quarantine", "P-WH", "LOT-A"), (dto.Lines[0].Quantity, dto.Lines[0].Unit, dto.Lines[0].NetWeightKg, dto.Lines[0].RestoredQualityStatus, dto.Lines[0].PalletReference, dto.Lines[0].LotNumber));

        var after = await StateAsync(_seed.ApprovedBox);
        // Oorspronkelijke regel en Dispatch-mutaties onaangeroerd.
        Assert.Equal((40m, 160m, 25m, 100m, before.Original.Version), (after.Original.Quantity, after.Original.NetWeightKg, after.Original.DispatchedQuantity, after.Original.DispatchedNetWeightKg, after.Original.Version));
        Assert.Equal(2, after.Movements.Count(m => m.Type == "Dispatch"));
        // Nieuwe inhoud in quarantaine op dezelfde pallet, zelfde partij en bron, exact de verzonden hoeveelheid en kg, niets afgeboekt.
        var restored = Assert.Single(after.Restored);
        Assert.Equal((after.Original.PalletId, after.Original.SupplyCommitmentLineId, "LOT-A", 15m, "box", 60m, "Quarantine", 0m, 0m),
            (restored.PalletId, restored.SupplyCommitmentLineId, restored.LotNumber, restored.Quantity, restored.Unit, restored.NetWeightKg, restored.QualityStatus, restored.DispatchedQuantity, restored.DispatchedNetWeightKg));
        Assert.Equal(dto.Lines[0].Id, restored.OriginCorrectionLineId);
        var compensation = Assert.Single(after.Movements, m => m.Type == "Correction");
        Assert.Equal((15m, "box", 60m, restored.Id, r2.Id, dto.Lines[0].Id), (compensation.Quantity, compensation.Unit, compensation.NetWeightKg, compensation.PalletContentLineId, compensation.StockReservationId, compensation.SalesDispatchCorrectionLineId));
        // Reserveringen: R1 blijft Consumed, R2 is Corrected (eindtoestand) met tijdstip; geen Active reservering hersteld.
        Assert.Equal("Consumed", after.Reservations.Single(r => r.Id == r1.Id).Status);
        var corrected = after.Reservations.Single(r => r.Id == r2.Id);
        Assert.Equal(("Corrected", 2), (corrected.Status, corrected.Version));
        Assert.NotNull(corrected.CorrectedAt);
        Assert.DoesNotContain(after.Reservations, r => r.Status == "Active");
        // Klantvraag heropend: 10 verzonden, 0 gereserveerd, 15 open, niet meer vervuld.
        order = await OrderAsync();
        var line = order.Lines.Single(l => l.Id == _boxLine);
        Assert.Equal((10m, 0m, 15m, false, false), (line.DispatchedQuantity, line.ReservedQuantity, line.OpenQuantity, line.Fulfilled, order.Fulfilled));
        var correctedDto = line.Reservations.Single(r => r.Id == r2.Id);
        Assert.Equal(("Corrected", dto.Id), (correctedDto.Status, correctedDto.CorrectionId));
        Assert.NotNull(correctedDto.CorrectedAt);
        // Audit: orderevent met requestId en bevestiging; per regel ContentLineAdded (herkomst) en ReservationCorrected zonder RequestId.
        var orderEvent = Assert.Single(after.OrderEvents, e => e.EventType == "SalesOrderDispatchCorrected");
        Assert.Equal(request.RequestId, orderEvent.RequestId);
        using (var snapshot = JsonDocument.Parse(orderEvent.NewValue!))
        {
            Assert.True(snapshot.RootElement.GetProperty("goodsNeverLeftWarehouseConfirmedByUser").GetBoolean());
            Assert.Equal(1, snapshot.RootElement.GetProperty("correctedReservationCount").GetInt32());
            Assert.Equal(60m, snapshot.RootElement.GetProperty("restoredNetWeightKg").GetDecimal());
        }
        var added = Assert.Single(after.PalletEvents, e => e.EventType == "ContentLineAdded" && e.PalletContentLineId == restored.Id);
        Assert.Contains("DispatchCorrection", added.NewValue);
        Assert.Null(added.RequestId);
        var correctedEvent = Assert.Single(after.PalletEvents, e => e.EventType == "ReservationCorrected");
        Assert.Contains(request.RequestId.ToString()!, correctedEvent.NewValue);
        // Pallet blijft Received/Warehouse; verzendingenlijst toont de correctie.
        await using var db = _factory.CreateContext();
        var pallet = (await new PalletService(db).GetAsync(_seed.WarehousePalletId)).Value;
        Assert.Equal(("Received", "Warehouse"), (pallet.LogisticsStatus, pallet.LocationContext));
        var restoredDto = pallet.ContentLines.Single(c => c.Id == restored.Id);
        Assert.Equal(("DispatchCorrection", dto.Lines[0].Id), (restoredDto.Origin, restoredDto.OriginCorrectionLineId));
        Assert.Equal("Receipt", pallet.ContentLines.Single(c => c.Id == _seed.ApprovedBox).Origin);
        var dispatches = (await new SalesDispatchService(db).ListAsync(_orderId)).Value;
        Assert.Null(dispatches.Single(d => d.Id == d1.Id).Correction);
        Assert.Equal(dto.Id, dispatches.Single(d => d.Id == d2.Id).Correction!.Id);
    }

    [Fact]
    public async Task Availability_After_Correction_Counts_Restored_Goods_As_Quarantine_Not_As_Registration_And_Sellable_Only_After_Inspection()
    {
        await ArrangeAsync(boxQuantity: 25m);
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var r2 = await ReservedAsync(_seed.ApprovedBox, 15m, 60m);
        await DispatchedAsync(r1);
        var d2 = await DispatchedAsync(r2);
        await using var db = _factory.CreateContext();
        var availability = new AvailabilityService(db);
        AvailabilityQuery Query() => new() { ProductVariantId = _seed.AvocadoId, Route = "Warehouse", Unit = "box" };
        var beforeSupply = (await availability.GetOverviewAsync(Query())).Value.Variants.Single().WarehouseSupply.Single();
        var beforeDrill = (await availability.GetVariantAsync(_seed.AvocadoId, Query())).Value;
        var registeredBefore = beforeDrill.SourceLines.Single(s => s.SupplyCommitmentLineId == beforeDrill.ContentLines.Single(c => c.PalletContentLineId == _seed.ApprovedBox).SupplyCommitmentLineId).RegisteredQuantity;

        Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(d2.Id, Correct(await OrderAsync(), d2))).Status);

        var supply = (await availability.GetOverviewAsync(Query())).Value.Variants.Single().WarehouseSupply.Single();
        // Nu beschikbaar ongewijzigd (teruggeboekt is quarantaine), quarantaine +15, registratie tegen de bron ongewijzigd.
        Assert.Equal(beforeSupply.AvailableBeforeReservations.Quantity, supply.AvailableBeforeReservations.Quantity);
        Assert.Equal(beforeSupply.Warehouse.Quarantine.Quantity + 15m, supply.Warehouse.Quarantine.Quantity);
        Assert.Equal(beforeSupply.Warehouse.Quarantine.NetWeightKg + 60m, supply.Warehouse.Quarantine.NetWeightKg);
        var drill = (await availability.GetVariantAsync(_seed.AvocadoId, Query())).Value;
        var restored = drill.ContentLines.Single(c => c.Origin == "DispatchCorrection");
        Assert.Equal((15m, 15m, 0m, false, "Quarantine"), (restored.Quantity, restored.OnHandQuantity, restored.AvailableQuantity, restored.SellableNow, restored.QualityStatus));
        Assert.Equal(registeredBefore, drill.SourceLines.Single(s => s.SupplyCommitmentLineId == restored.SupplyCommitmentLineId).RegisteredQuantity);
        Assert.Empty(supply.Warnings.Where(w => w.Code == "OVER_REGISTERED"));

        // Keuring: Approved maakt de 15 box verkoopbaar; reserveren erop kan daarna.
        var pallets = new PalletService(db);
        var approve = await pallets.ChangeQualityStatusAsync(_seed.WarehousePalletId, restored.PalletContentLineId, new ChangeQualityStatusDto
        {
            RequestId = Guid.NewGuid(), ExpectedContentLineVersion = 0, ToStatus = "Approved", ActorReference = "qc-1"
        });
        Assert.Equal(ServiceResultStatus.Ok, approve.Status);
        var afterInspection = (await availability.GetOverviewAsync(Query())).Value.Variants.Single().WarehouseSupply.Single();
        Assert.Equal(beforeSupply.AvailableBeforeReservations.Quantity + 15m, afterInspection.AvailableBeforeReservations.Quantity);
        var reservable = (await new SalesOrderService(db).GetReservableStockAsync(_orderId, _boxLine)).Value;
        Assert.Equal(15m, reservable.Single(s => s.PalletContentLineId == restored.PalletContentLineId).AvailableQuantity);
    }

    [Fact]
    public async Task Chain_Dispatch_Correction_Inspection_Reserve_Dispatch_Correction_Reconciles_Each_Correction_Separately()
    {
        await ArrangeAsync(boxQuantity: 15m);
        var r1 = await ReservedAsync(_seed.ApprovedBox, 15m, 60m);
        var d1 = await DispatchedAsync(r1);
        var c1 = await CorrectAsync(d1.Id, Correct(await OrderAsync(), d1));
        Assert.Equal(ServiceResultStatus.Created, c1.Status);
        var restored1 = c1.Value.Lines[0].RestoredPalletContentLineId;
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new PalletService(db).ChangeQualityStatusAsync(_seed.WarehousePalletId, restored1, new ChangeQualityStatusDto
            {
                RequestId = Guid.NewGuid(), ExpectedContentLineVersion = 0, ToStatus = "Approved", ActorReference = "qc-1"
            })).Status);
        }
        var r2 = await ReservedAsync(restored1, 15m, 60m);
        var d2 = await DispatchedAsync(r2);
        var c2 = await CorrectAsync(d2.Id, Correct(await OrderAsync(), d2, reason: "Nogmaals verkeerd"));
        Assert.Equal(ServiceResultStatus.Created, c2.Status);
        var restored2 = c2.Value.Lines[0].RestoredPalletContentLineId;

        await using var verify = _factory.CreateContext();
        // Oorspronkelijke regel: Dispatch-mutaties = afgeboekt (15/60); restored1: één Dispatch-mutatie = afgeboekt 15/60, ontvangst 15/60 = Correction-mutatie 1.
        foreach (var (id, expectedDispatched) in new[] { (_seed.ApprovedBox, 15m), (restored1, 15m), (restored2, 0m) })
        {
            var content = await verify.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == id);
            var dispatchSum = (await verify.StockMovements.AsNoTracking().Where(m => m.PalletContentLineId == id && m.Type == "Dispatch").Select(m => m.Quantity).ToListAsync()).Sum();
            Assert.Equal(expectedDispatched, content.DispatchedQuantity);
            Assert.Equal(content.DispatchedQuantity, dispatchSum);
        }
        foreach (var (restored, correctionLineId) in new[] { (restored1, c1.Value.Lines[0].Id), (restored2, c2.Value.Lines[0].Id) })
        {
            var content = await verify.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == restored);
            var compensation = await verify.StockMovements.AsNoTracking().SingleAsync(m => m.SalesDispatchCorrectionLineId == correctionLineId);
            var correctionLine = await verify.SalesDispatchCorrectionLines.AsNoTracking().SingleAsync(l => l.Id == correctionLineId);
            Assert.Equal((content.Quantity, content.NetWeightKg), (compensation.Quantity, compensation.NetWeightKg));
            Assert.Equal((correctionLine.Quantity, correctionLine.NetWeightKg), (compensation.Quantity, compensation.NetWeightKg));
            Assert.Equal(restored, compensation.PalletContentLineId);
            Assert.Equal(correctionLineId, content.OriginCorrectionLineId);
        }
        Assert.Equal(2, await verify.StockMovements.CountAsync(m => m.Type == "Dispatch"));
        Assert.Equal(2, await verify.StockMovements.CountAsync(m => m.Type == "Correction"));
        Assert.Equal(new[] { "Corrected", "Corrected" }, (await verify.StockReservations.Where(r => r.SalesOrderLineId != null).OrderBy(r => r.Id).Select(r => r.Status).ToListAsync()).ToArray());
        var order = await OrderAsync();
        Assert.Equal((0m, 0m, 15m, false), (order.Lines.Single(l => l.Id == _boxLine).DispatchedQuantity, order.Lines.Single(l => l.Id == _boxLine).ReservedQuantity, order.Lines.Single(l => l.Id == _boxLine).OpenQuantity, order.Fulfilled));
        // Registratie tegen de bron: alleen de fysieke ontvangsten tellen (restored1 en restored2 niet).
        var drill = (await new AvailabilityService(verify).GetVariantAsync(_seed.AvocadoId, new AvailabilityQuery { Route = "Warehouse", Unit = "box" })).Value;
        var source = drill.SourceLines.Single(s => s.SupplyCommitmentLineId == drill.ContentLines.Single(c => c.PalletContentLineId == _seed.ApprovedBox).SupplyCommitmentLineId);
        Assert.Equal(83m, source.RegisteredQuantity);
    }

    [Fact]
    public async Task Gross_Weight_Check_Uses_On_Hand_Weights_So_Correction_Fits_Where_Historical_Receipts_Would_Not()
    {
        await ArrangeAsync(boxQuantity: 40m);
        // Pallet P-WH: inhoud in totaal 40+10 colli... gebruik P-WH2 (alleen LOT-C 20 box / 80 kg) met gemeten tara 25 en bruto 105 (= 25 + 80).
        await using (var db = _factory.CreateContext())
        {
            var pallet = await db.Pallets.SingleAsync(p => p.Id == _seed.SecondWarehousePalletId);
            pallet.TareKg = 25m;
            pallet.GrossKg = 105m;
            await db.SaveChangesAsync();
        }
        var r = await ReservedAsync(_seed.SecondPalletApprovedBox, 20m, 80m);
        var d = await DispatchedAsync(r);

        // Historisch gesommeerd zou het 25 + 80 (ontvangst) + 80 (terugboeking) = 185 kg zijn, boven bruto 105; op voorraad is het 25 + 0 + 80 = 105 (past exact).
        var result = await CorrectAsync(d.Id, Correct(await OrderAsync(), d));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        await using var verify = _factory.CreateContext();
        var pallet2 = (await new PalletService(verify).GetAsync(_seed.SecondWarehousePalletId)).Value;
        Assert.Equal((80m, 80m), (pallet2.ContentNetKg, pallet2.ReceivedNetKg)); // fysiek 80 (alleen de herstelde regel), historisch ontvangen 80 (LOT-C)
        // Daarna past er niets meer bij: een ontvangst van 1 kg extra wordt geweigerd op basis van voorraad.
        var add = await new PalletService(verify).AddContentLineAsync(_seed.SecondWarehousePalletId, new AddPalletContentLineDto
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(verify, _seed.SecondPalletApprovedBox), LotNumber = "LOT-NEW",
            Quantity = 1m, Unit = "box", NetWeightKg = 1m, ActorReference = "wh-1"
        });
        Assert.Equal(ServiceResultStatus.Conflict, add.Status);
        Assert.Contains("on-hand", add.Message);
    }

    // ======================== Afwijzingen ========================

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("version-missing", "expectedOrderVersion")]
    [InlineData("actor-missing", "actorReference")]
    [InlineData("reason-missing", "reason")]
    [InlineData("reason-blank", "reason")]
    [InlineData("not-confirmed-false", "goodsNeverLeftWarehouse")]
    [InlineData("not-confirmed-null", "goodsNeverLeftWarehouse")]
    [InlineData("lines-missing", "lines")]
    [InlineData("line-duplicate", "lines[1]")]
    public async Task Correction_Rejects_Invalid_Input_Including_Missing_Confirmation_And_Changes_Nothing(string invalidCase, string key)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var d = await DispatchedAsync(r);
        var request = Correct(await OrderAsync(), d);
        switch (invalidCase)
        {
            case "request-id-missing": request.RequestId = null; break;
            case "version-missing": request.ExpectedOrderVersion = null; break;
            case "actor-missing": request.ActorReference = ""; break;
            case "reason-missing": request.Reason = null; break;
            case "reason-blank": request.Reason = "   "; break;
            case "not-confirmed-false": request.GoodsNeverLeftWarehouse = false; break;
            case "not-confirmed-null": request.GoodsNeverLeftWarehouse = null; break;
            case "lines-missing": request.Lines = null; break;
            default: request.Lines.Add(new CreateSalesDispatchLineDto { StockReservationId = r.Id, ExpectedReservationVersion = 1 }); break;
        }

        var result = await CorrectAsync(d.Id, request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        var state = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((0, 0), (state.Corrections, state.Restored.Count));
        Assert.Equal("Consumed", state.Reservations.Single().Status);
    }

    [Fact]
    public async Task Correction_Of_Unknown_Or_Foreign_Dispatch_Is_NotFound()
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var d = await DispatchedAsync(r);
        var (otherOrder, _, _) = await SalesTestData.SeedOrderAsync(_factory, _seed);
        Assert.Equal(ServiceResultStatus.NotFound, (await CorrectAsync(999_999, Correct(await OrderAsync(), d))).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await CorrectAsync(d.Id, Correct(await OrderAsync(), d), orderId: otherOrder)).Status);
    }

    [Theory]
    [InlineData("stale-order-version")]
    [InlineData("stale-reservation-version")]
    [InlineData("already-corrected")]
    [InlineData("partial-line-set")]
    [InlineData("extra-line")]
    [InlineData("pallet-left-warehouse")]
    [InlineData("order-cancelled")]
    public async Task Correction_Business_Rule_Violations_Are_Conflict_Without_Change(string scenario)
    {
        await ArrangeAsync(boxQuantity: 40m);
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var r2 = await ReservedAsync(_seed.SecondPalletApprovedBox, 5m, 20m);
        var d = await DispatchedAsync(r1, r2);
        var request = Correct(await OrderAsync(), d);
        switch (scenario)
        {
            case "stale-order-version": request.ExpectedOrderVersion += 1; break;
            case "stale-reservation-version": request.Lines[0].ExpectedReservationVersion += 1; break;
            case "already-corrected":
                Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(d.Id, Correct(await OrderAsync(), d))).Status);
                request = Correct(await OrderAsync(), d);
                break;
            case "partial-line-set": request.Lines.RemoveAt(1); break;
            case "extra-line":
            {
                var extra = await ReservedAsync(_seed.ApprovedColli, 1m, 5.5m, _colliLine, "colli");
                request.Lines.Add(new CreateSalesDispatchLineDto { StockReservationId = extra.Id, ExpectedReservationVersion = extra.Version });
                break;
            }
            case "pallet-left-warehouse":
            {
                // De tweede pallet (alleen LOT-C, nu volledig verbruikt) gaat volgens de logistiek weg: dan is de aanname "nog in het magazijn" niet te maken.
                await using var db = _factory.CreateContext();
                var pallet = await db.Pallets.SingleAsync(p => p.Id == _seed.SecondWarehousePalletId);
                var change = await new PalletService(db).ChangeLogisticsStatusAsync(pallet.Id, new ChangeLogisticsStatusDto
                {
                    RequestId = Guid.NewGuid(), ExpectedPalletVersion = pallet.Version, ToStatus = "Dispatched", LocationContext = "InTransit", ActorReference = "wh-1"
                });
                Assert.Equal(ServiceResultStatus.Ok, change.Status);
                request = Correct(await OrderAsync(), d);
                break;
            }
            case "order-cancelled":
            {
                // Annuleren kan pas zonder Consumed; simuleer een order die intussen niet meer Open is via de guardvrije weg: eerst corrigeren is de enige weg.
                // Daarom: een tweede, lege order die Cancelled is, krijgt dit niet; dit scenario test de orderstatuscontrole via een directe statuswijziging.
                await using var db = _factory.CreateContext();
                var order = await db.SalesOrders.FindAsync(_orderId);
                order!.Status = "Cancelled";
                order.CancelledAt = DateTime.UtcNow;
                order.CancelReason = "test";
                await db.SaveChangesAsync();
                request = Correct(await OrderAsync(), d);
                break;
            }
        }
        var before = await StateAsync(_seed.ApprovedBox);

        var result = await CorrectAsync(d.Id, request);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var after = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((before.Corrections, before.Restored.Count, before.Movements.Count), (after.Corrections, after.Restored.Count, after.Movements.Count));
        Assert.Equal(before.Reservations.Select(x => (x.Status, x.Version)), after.Reservations.Select(x => (x.Status, x.Version)));
        if (scenario == "pallet-left-warehouse") Assert.Contains("receipt flow", result.Message);
        if (scenario == "already-corrected") Assert.Contains("already been corrected", result.Message);
    }

    // ======================== Idempotentie ========================

    [Fact]
    public async Task Exact_Replay_Never_Restores_Stock_Twice()
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var d = await DispatchedAsync(r);
        var request = Correct(await OrderAsync(), d);
        var first = await CorrectAsync(d.Id, request);
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        var before = await StateAsync(_seed.ApprovedBox);

        var retry = await CorrectAsync(d.Id, request);

        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        var after = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((before.Corrections, before.Restored.Count, before.Movements.Count, before.OrderEvents.Count, before.PalletEvents.Count),
            (after.Corrections, after.Restored.Count, after.Movements.Count, after.OrderEvents.Count, after.PalletEvents.Count));
        Assert.Equal(before.Reservations.Select(x => x.Version), after.Reservations.Select(x => x.Version));
        Assert.Equal(10m, Assert.Single(after.Restored).Quantity);
    }

    [Theory]
    [InlineData("other-order")]
    [InlineData("other-dispatch")]
    [InlineData("other-actor")]
    [InlineData("other-reason")]
    [InlineData("other-order-version")]
    [InlineData("other-reservation-version")]
    public async Task Reused_Correction_RequestId_With_Different_Payload_Is_Conflict(string change)
    {
        await ArrangeAsync();
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var r2 = await ReservedAsync(_seed.SecondPalletApprovedBox, 5m, 20m);
        var d1 = await DispatchedAsync(r1);
        var d2 = await DispatchedAsync(r2);
        var request = Correct(await OrderAsync(), d1);
        Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(d1.Id, request)).Status);
        var dispatchId = d1.Id;
        var orderId = _orderId;
        switch (change)
        {
            case "other-order": (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, _seed); break;
            case "other-dispatch": dispatchId = d2.Id; break;
            case "other-actor": request.ActorReference = "office-2"; break;
            case "other-reason": request.Reason = "Andere reden"; break;
            case "other-order-version": request.ExpectedOrderVersion += 1; break;
            default: request.Lines[0].ExpectedReservationVersion += 1; break;
        }

        var result = await CorrectAsync(dispatchId, request, orderId);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal(1, (await StateAsync(_seed.ApprovedBox)).Corrections);
    }

    // ======================== Gelijktijdigheid ========================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_Concurrent_Corrections_Of_One_Dispatch_Restore_Stock_Once(bool sameRequestId)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var d = await DispatchedAsync(r);
        var order = await OrderAsync();
        var first = Correct(order, d);
        var second = sameRequestId ? Correct(order, d) : Correct(order, d);
        if (sameRequestId) second.RequestId = first.RequestId;
        ServiceResultStatus inner = default;

        var outer = (await CorrectAsync(d.Id, first, interceptors: new ConcurrentSaveInterceptor(async () => inner = (await CorrectAsync(d.Id, second)).Status))).Status;

        Assert.Equal(ServiceResultStatus.Created, inner);
        Assert.Equal(sameRequestId ? ServiceResultStatus.Ok : ServiceResultStatus.Conflict, outer);
        var state = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((1, 1, 1), (state.Corrections, state.Restored.Count, state.Movements.Count(m => m.Type == "Correction")));
    }

    [Theory]
    [InlineData("correction-saves-last")]
    [InlineData("dispatch-saves-last")]
    public async Task Correction_And_Dispatch_On_The_Same_Order_Have_Exactly_One_Winner(string order)
    {
        await ArrangeAsync(boxQuantity: 40m);
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var r2 = await ReservedAsync(_seed.SecondPalletApprovedBox, 5m, 20m);
        var d1 = await DispatchedAsync(r1);
        var current = await OrderAsync();
        CreateSalesDispatchDto Dispatch() => new()
        {
            RequestId = Guid.NewGuid(), ExpectedOrderVersion = current.Version, ActorReference = "office-1", Reference = "VRB-2",
            Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = r2.Id, ExpectedReservationVersion = r2.Version } }
        };
        async Task<ServiceResultStatus> DispatchAsync(params IInterceptor[] interceptors)
        {
            await using var db = _factory.CreateContext(interceptors);
            return (await new SalesDispatchService(db).DispatchAsync(_orderId, Dispatch())).Status;
        }
        ServiceResultStatus inner = default, outer;
        if (order == "correction-saves-last")
        {
            outer = (await CorrectAsync(d1.Id, Correct(current, d1), interceptors: new ConcurrentSaveInterceptor(async () => inner = await DispatchAsync()))).Status;
        }
        else
        {
            outer = await DispatchAsync(new ConcurrentSaveInterceptor(async () => inner = (await CorrectAsync(d1.Id, Correct(current, d1))).Status));
        }
        Assert.Equal(ServiceResultStatus.Created, inner);
        Assert.Equal(ServiceResultStatus.Conflict, outer);
    }

    [Theory]
    [InlineData("correction-saves-last")]
    [InlineData("block-saves-last")]
    public async Task Correction_And_Quality_Block_On_The_Original_Content_Collide_Through_The_Pallet_Version(string order)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var d = await DispatchedAsync(r);
        var current = await OrderAsync();
        async Task<ServiceResultStatus> BlockAsync(params IInterceptor[] interceptors)
        {
            await using var read = _factory.CreateContext();
            var line = await read.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == _seed.ApprovedBox);
            await using var db = _factory.CreateContext(interceptors);
            return (await new PalletService(db).ChangeQualityStatusAsync(line.PalletId, line.Id, new ChangeQualityStatusDto
            {
                RequestId = Guid.NewGuid(), ExpectedContentLineVersion = line.Version, ToStatus = "Blocked", ActorReference = "qc-1", Reason = "Claim"
            })).Status;
        }
        ServiceResultStatus inner = default, outer;
        if (order == "correction-saves-last")
        {
            outer = (await CorrectAsync(d.Id, Correct(current, d), interceptors: new ConcurrentSaveInterceptor(async () => inner = await BlockAsync()))).Status;
        }
        else
        {
            outer = await BlockAsync(new ConcurrentSaveInterceptor(async () => inner = (await CorrectAsync(d.Id, Correct(current, d))).Status));
        }
        // De palletversie beschermt beide kanten: precies één wint; na herladen kan de ander opnieuw.
        Assert.True(inner is ServiceResultStatus.Ok or ServiceResultStatus.Created);
        Assert.Equal(ServiceResultStatus.Conflict, outer);
    }

    [Fact]
    public async Task After_A_Full_Correction_The_Order_Can_Be_Cancelled_And_Corrected_Reservations_Stay_Terminal()
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var d = await DispatchedAsync(r);
        Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(d.Id, Correct(await OrderAsync(), d))).Status);
        await using var db = _factory.CreateContext();
        var release = await new StockReservationService(db).ReleaseAsync(r.Id, new ReleaseStockReservationDto { RequestId = Guid.NewGuid(), ExpectedReservationVersion = 2, ActorReference = "office-1", Reason = "x" });
        Assert.Equal(ServiceResultStatus.Conflict, release.Status);
        var order = (await new SalesOrderService(db).GetAsync(_orderId)).Value;
        var cancel = await new SalesOrderService(db).CancelAsync(_orderId, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = order.Version, ActorReference = "office-1", Reason = "Afgezegd na correctie" });
        Assert.Equal(ServiceResultStatus.Ok, cancel.Status);
        Assert.Equal("Corrected", (await db.StockReservations.AsNoTracking().SingleAsync(x => x.Id == r.Id)).Status);
    }
}
