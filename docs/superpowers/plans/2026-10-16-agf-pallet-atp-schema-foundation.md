# AGF Pallet & ATP Schema Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct the unused availability schema so it records explicit AGF units and weights, then add pallet/content/audit schema needed for traceable ATP flows.

**Architecture:** Keep the availability domain additive and leave all controllers, services and frontend screens untouched. First make existing supply, movement and reservation quantities decimal and explicitly unit/weight-aware. Then add a pallet as a logistics container, atomic pallet content lines as the physical stock/lot carrier, and immutable audit events. Future API work will enforce operational workflows; this plan only provides safe relational foundations.

**Tech Stack:** ASP.NET Core 9, EF Core 9.0.7, PostgreSQL/Npgsql, EF Core SQLite 9.0.7, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-16-agf-pallet-atp-design.md`  
**Dependency:** Merge PR #12 containing the spec before this plan is merged or executed.

## Global Constraints

- Supplier invoices remain administrative and never write availability.
- Never derive weight from product size, packaging text, pallet count or historical defaults.
- New availability quantities and weights use `decimal(18,3)`.
- Every new availability write will later require an explicit package unit and actual net kg; this schema task must not invent values for old rows.
- A final lot is mandatory for physical pallet content; an expected source line may retain a provisional or unknown lot until receipt.
- All new foreign keys use `Restrict`; no cascade deletion of traceability or audit records.
- Existing legacy `Inventory`, `Shipment` and `OrderLineAllocation` remain untouched.
- Never delete/reset a database, rewrite existing history or add destructive migration SQL.
- Do not expose the new schema in a controller or frontend in this plan.
- Preserve the existing CI commands and add focused SQLite relational tests.

## Review Focus

- Existing availability tables with unknown historical weight/unit data: migration preserves rows and leaves no invented net weight or unit. **Task 1.**
- A user attempts zero or negative package quantity/net kg on new pallet content: database rejects it. **Task 2.**
- Two pallets use the same scanned reference or SSCC: unique index rejects it. **Task 2.**
- A mixed pallet maps each content line to exactly one source line and one lot; the same source line may be spread over multiple pallets. **Task 2.**
- Deleting a pallet, content line or source line with traceability/audit dependents: database rejects it. **Task 2.**

---

## Delivery boundary

This plan is two small schema-only PRs. Neither makes stock available, creates a receipt endpoint, alters current product screens or changes existing test data. The existing `2026-10-15-p1-inventory-availability-rollout.md` is not executable past its completed Tasks 1–2; write a replacement API/UI rollout plan only after both PRs below are merged.

### Task 1: Add explicit decimal quantities, units and net weights

**Files:**
- Modify: `AgfAdmin/Models/ProductVariant.cs`
- Modify: `AgfAdmin/Models/SupplyCommitmentLine.cs`
- Modify: `AgfAdmin/Models/StockMovement.cs`
- Modify: `AgfAdmin/Models/StockReservation.cs`
- Modify: `AgfAdmin/Data/AppDbContext.cs`
- Create: `AgfAdmin/Migrations/<timestamp>_AddAvailabilityUnitsAndWeights.cs`
- Modify: `AgfAdmin/Migrations/<timestamp>_AddAvailabilityUnitsAndWeights.Designer.cs`
- Modify: `AgfAdmin/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `AgfAdmin.Tests/Models/InventoryAvailabilityModelTests.cs`

**Consumes:** the merged availability models from PR #11.

**Produces:** decimal, explicit unit and net-weight fields for later service/API work. No existing availability record receives an implied unit or weight.

**Interfaces:**

```csharp
// ProductVariant: nullable during legacy transition; new availability writers choose one explicitly.
public string? AvailabilityUnit { get; set; } // "Kilogram" or "Package"

// SupplyCommitmentLine
public decimal ExpectedQuantity { get; set; }
public string? ExpectedUnit { get; set; }      // box, colli, tray, piece or dozen
public decimal? ExpectedNetWeightKg { get; set; }
public string? LotNumber { get; set; }         // provisional until physical content exists

// StockMovement and StockReservation
public decimal Quantity { get; set; }
public string? Unit { get; set; }
public decimal? NetWeightKg { get; set; }
```

Use Fluent configuration, not a size-to-weight rule. Configure every decimal field as `decimal(18,3)`; cap unit strings at 32 characters. `AvailabilityUnit`, `ExpectedUnit`, `ExpectedNetWeightKg`, `Unit` and `NetWeightKg` stay nullable in this transition migration because existing rows cannot be safely backfilled. Later DTO/service validation makes them mandatory for new writes.

- [ ] **Step 1: Write failing SQLite tests for decimal precision and no invented backfill**

In `InventoryAvailabilityModelTests.cs`, add:
- `Availability_Quantity_And_NetWeight_RoundTrip_With_Three_Decimals`: save `12.345m` quantity and `67.890m` net kg on a commitment line, movement and reservation; reload and assert the exact decimal values.
- `Legacy_Availability_Metadata_Can_Remain_Unknown_Until_A_New_Write`: save the minimum currently valid graph without `AvailabilityUnit`, expected unit or net-weight metadata; reload and assert each added metadata value is null.

Update existing seeded quantities and assertions from integer literals to decimal literals.

- [ ] **Step 2: Run focused tests to verify failure**

Run: `dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~InventoryAvailabilityModelTests`

Expected: FAIL because the decimal/unit/weight properties do not exist or integer properties cannot accept decimal assertions.

- [ ] **Step 3: Add the properties and Fluent precision configuration**

Implement the interfaces above in the four models. In `AppDbContext.OnModelCreating`, configure all six quantity/weight properties with `HasPrecision(18, 3)`, and configure the unit fields with `HasMaxLength(32)`. Do not add a default value, conversion rule, check constraint or write path that guesses weight or packaging.

- [ ] **Step 4: Generate and inspect the corrective migration**

Run:

```bash
dotnet ef migrations add AddAvailabilityUnitsAndWeights --project AgfAdmin/AgfAdmin.csproj
dotnet ef migrations script --project AgfAdmin/AgfAdmin.csproj
```

The migration may alter integer quantity columns to decimal and add nullable metadata columns. It must not contain `DROP TABLE`, `DELETE`, `TRUNCATE`, `EnsureDeleted` or a default net-weight/unit backfill.

- [ ] **Step 5: Run focused verification**

Run:

```bash
dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~InventoryAvailabilityModelTests
dotnet ef migrations has-pending-model-changes --project AgfAdmin/AgfAdmin.csproj
```

Expected: all model tests pass and EF reports no pending model changes.

- [ ] **Step 6: Commit and open one PR**

```bash
git add AgfAdmin/Models/ProductVariant.cs AgfAdmin/Models/SupplyCommitmentLine.cs AgfAdmin/Models/StockMovement.cs AgfAdmin/Models/StockReservation.cs AgfAdmin/Data/AppDbContext.cs AgfAdmin/Migrations AgfAdmin.Tests/Models/InventoryAvailabilityModelTests.cs
git commit -m "feat: add explicit availability units and weights"
```

### Task 2: Add pallet contents and traceability audit schema

**Files:**
- Create: `AgfAdmin/Models/Pallet.cs`
- Create: `AgfAdmin/Models/PalletContentLine.cs`
- Create: `AgfAdmin/Models/PalletAuditEvent.cs`
- Modify: `AgfAdmin/Models/StockMovement.cs`
- Modify: `AgfAdmin/Models/StockReservation.cs`
- Modify: `AgfAdmin/Data/AppDbContext.cs`
- Create: `AgfAdmin/Migrations/<timestamp>_AddPalletTraceability.cs`
- Modify: `AgfAdmin/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `AgfAdmin.Tests/Models/InventoryAvailabilityModelTests.cs`

**Consumes:** Task 1 decimal/unit fields.

**Produces:** pallet/container, atomic physical contents, optional source links for later physical movements/reservations, and append-only pallet audit records.

**Interfaces:**

```csharp
public sealed class Pallet
{
    public int Id { get; set; }
    public string Reference { get; set; }          // required unique scanable reference
    public string? Sscc { get; set; }              // optional, unique when present; never generated as a fake SSCC
    public string? PalletType { get; set; }
    public decimal? TareKg { get; set; }
    public decimal? GrossKg { get; set; }
    public decimal? DeclaredNetKg { get; set; }    // separately measured/sticker value
    public string LogisticsStatus { get; set; }    // Expected, InTransit, Received, Dispatched, Delivered, Cancelled
    public string LocationContext { get; set; }    // Supplier, Warehouse, Crossdock, InTransit, Customer, External
    public string? WarehouseLocation { get; set; }
    public int? StockReceiptId { get; set; }
    public ICollection<PalletContentLine> ContentLines { get; set; }
}

public sealed class PalletContentLine
{
    public int Id { get; set; }
    public int PalletId { get; set; }
    public int SupplyCommitmentLineId { get; set; }
    public string LotNumber { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; }               // box, colli, tray, piece or dozen
    public decimal NetWeightKg { get; set; }
    public string QualityStatus { get; set; }      // Quarantine, Approved, Blocked, Rejected
    public string? QualityReason { get; set; }
}

public sealed class PalletAuditEvent
{
    public int Id { get; set; }
    public int? PalletId { get; set; }
    public int? PalletContentLineId { get; set; }
    public string EventType { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public string? ActorReference { get; set; }
    public DateTime OccurredAt { get; set; }
}
```

Add nullable `PalletContentLineId` foreign keys to `StockMovement` and `StockReservation`. They remain nullable until later expected/transit flows create physical content. `PalletAuditEvent` must have a database check constraint requiring at least one target (`PalletId` or `PalletContentLineId`).

Configure all strings with explicit sensible maximum lengths; every new decimal uses `decimal(18,3)`. Add database check constraints on `PalletContentLine.Quantity > 0` and `PalletContentLine.NetWeightKg > 0`, and restrict source-line, pallet, receipt, movement, reservation and audit deletes. Unique indexes: `Pallet.Reference` always; `Pallet.Sscc` when non-null.

Do not persist a mutable pallet net-total field: later services calculate it as `SUM(PalletContentLine.NetWeightKg)` and compare it to `DeclaredNetKg` under a configured tolerance.

- [ ] **Step 1: Write failing SQLite relational tests**

Add these tests to `InventoryAvailabilityModelTests.cs`:

- `Pallet_Rejects_Duplicate_Reference_And_Duplicate_Sscc`.
- `PalletContentLine_Requires_Pallet_SourceLine_Lot_Unit_And_Positive_Quantities`.
- `SourceLine_Can_Be_Split_Across_Multiple_Pallets_With_Distinct_ContentLines`.
- `PalletContentLine_Rejects_Zero_Quantity_Or_NetWeight`.
- `PalletAuditEvent_Requires_A_Pallet_Or_ContentLine_Target`.
- `Deleting_Pallet_Or_ContentLine_With_Traceability_Dependents_Is_Restricted`.

The test graph must use one pallet containing two lines of the same product variant but distinct lots/source lines, proving that content—not the pallet—is the traceability boundary.

- [ ] **Step 2: Run focused tests to verify failure**

Run: `dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~InventoryAvailabilityModelTests`

Expected: FAIL because the pallet models, DbSets, check constraints and foreign keys do not exist.

- [ ] **Step 3: Add models, DbSets and Fluent relational configuration**

Create the three models and register `Pallets`, `PalletContentLines` and `PalletAuditEvents`. Configure the interfaces above in `OnModelCreating`, including unique indexes, check constraints, max lengths, decimal precision and `DeleteBehavior.Restrict` relationships. Keep `ProductVariant` referenced through `SupplyCommitmentLine`; do not duplicate `ProductVariantId` on `PalletContentLine`.

- [ ] **Step 4: Generate and inspect the pallet migration**

Run:

```bash
dotnet ef migrations add AddPalletTraceability --project AgfAdmin/AgfAdmin.csproj
dotnet ef migrations script --project AgfAdmin/AgfAdmin.csproj
```

Expected: only additive pallet/audit tables, nullable foreign keys, indexes and check constraints. No delete/reset SQL and no cascade delete.

- [ ] **Step 5: Run full backend verification**

Run:

```bash
dotnet restore Software-AGF.sln
dotnet build Software-AGF.sln --no-restore --configuration Release
dotnet test Software-AGF.sln --no-build --configuration Release
dotnet ef migrations has-pending-model-changes --project AgfAdmin/AgfAdmin.csproj
rg -n "EnsureDeleted|EnsureCreated" AgfAdmin AgfAdmin.Tests -g '*.cs'
```

Expected: build has zero errors, all tests pass, EF has no pending model changes and the final scan has no matches.

- [ ] **Step 6: Commit and open one PR**

```bash
git add AgfAdmin/Models/Pallet.cs AgfAdmin/Models/PalletContentLine.cs AgfAdmin/Models/PalletAuditEvent.cs AgfAdmin/Models/StockMovement.cs AgfAdmin/Models/StockReservation.cs AgfAdmin/Data/AppDbContext.cs AgfAdmin/Migrations AgfAdmin.Tests/Models/InventoryAvailabilityModelTests.cs
git commit -m "feat: add pallet traceability schema"
```

## Self-review

- **Spec coverage:** Task 1 covers explicit decimal quantities, units, net weight and absence of invented size conversions. Task 2 covers pallets, atomic content lines, lots, quality/logistics/location metadata, auditable events and restrictive traceability relations. ATP calculation, status workflows, tolerance handling, receipts, sales and UI are deliberately deferred until this schema is merged and receive a separate replacement rollout plan.
- **Step scan:** every task has a focused red/green test cycle, a migration inspection, full verification and one reviewable PR boundary.
- **Type consistency:** all availability quantity/weight values are `decimal(18,3)`; source links use `SupplyCommitmentLineId`; optional physical allocation uses `PalletContentLineId`; future physical traceability resolves product variants through the source line.
- **Review focus:** each listed failure mode is mapped to Task 1 or Task 2 tests.
- **Proportion:** this plan contains two schema-only deliverables and does not prescribe service/controller bodies.
