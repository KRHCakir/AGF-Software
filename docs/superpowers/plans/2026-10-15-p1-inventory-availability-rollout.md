# Inventory Availability Rollout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace unsafe negative-backorder and invoice-driven stock updates with explicit expected, physical, reserved and direct-trade availability.

**Architecture:** Keep incoming invoices administrative: they never change availability. Introduce an additive availability domain (supply commitments, receipts, stock movements and reservations) and make it the only source for new stock flows. Migrate the screens and sales APIs only after the domain/service layer is tested; no application code may delete the existing test database or silently rewrite historical stock.

**Tech Stack:** ASP.NET Core 9, EF Core 9.0.7, PostgreSQL/Npgsql, xUnit, EF Core SQLite test provider, Next.js/TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-15-inventory-availability-design.md`

## Global Constraints

- Supplier invoices are administrative documents and must not create, update or delete stock/availability.
- Physical on-hand, expected/inbound, reserved and direct trade are distinct quantities in every API and screen.
- Expected quantity is never added to physical on-hand.
- The application must never create negative physical stock automatically.
- Receipt, reservation, release and dispatch must be idempotent and auditable.
- A direct trade must identify supplier/source and customer/destination and must never create Dutch warehouse stock.
- Preserve the existing database until the user explicitly authorises a one-time manual reset of its known test data; never add automatic deletion/reset code.
- Keep all existing P0 quality commands green; every data or status change receives focused tests.

## Review Focus

- Double-click/retry of the same receipt request: physical quantity is applied once and the second response reports the original receipt. **Task 3.**
- A sale that exceeds physical plus eligible expected quantity: reject it; do not create a negative inventory row. **Task 4.**
- Partial receipt of an expected line: physical increases only by the received part and the remainder stays expected. **Task 3.**
- Cancellation of a reserved order: reservation is released exactly once and availability returns. **Task 4.**
- Direct supplier-to-customer sale: commitment/reservation is visible but physical warehouse balance remains unchanged. **Task 6.**

---

## Delivery boundaries

This is intentionally six small PRs, not one rewrite. Tasks 1–5 form the warehouse-availability cutover. Task 6 adds direct trade only after warehouse behaviour is proven. Do not expose the new inventory page to daily users until Tasks 3–5 are merged together.

### Task 1: Remove invoice-driven stock changes

**Files:**
- Modify: `AgfAdmin/Controllers/IncomingInvoicesController.cs`
- Modify: `AgfAdmin.Tests/Controllers/IncomingInvoicesControllerTests.cs`
- Modify if required by the current UI contract: `agf-frontend/src/components/ManualInvoiceModal.tsx`

**Consumes:** existing reviewable invoice status policy from P1-1.

**Produces:** manual and AI/approval invoice flows that are administrative-only; a receipt/supply flow is the only future availability writer.

- [ ] **Step 1: Write failing controller tests for manual invoices**

Add tests named `CreateManualInvoice_Creates_Draft_Without_Changing_Inventory` and `Approve_ManualInvoice_Does_Not_Change_Inventory`. Seed an `Inventory` row with quantity `10`, create a valid manual invoice line with a `ProductVariantId`, and assert the row remains `10`; assert the created invoice status is `Draft`.

- [ ] **Step 2: Run the focused tests to verify the current failure**

Run: `dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~IncomingInvoicesControllerTests`

Expected: FAIL because manual creation currently sets `Approved` and increments/creates `Inventory` rows.

- [ ] **Step 3: Make manual invoice creation administrative-only**

In `CreateManualInvoice`, set `IncomingInvoice.Status` to `Draft` and delete the per-line lookup/add/update of `Inventory`. Keep line totals, VAT totals, vendor validation behaviour and `CreatedAtAction` response unchanged. Do not call a future stock service from this controller.

- [ ] **Step 4: Run focused and full backend tests**

Run: `dotnet test Software-AGF.sln --configuration Release`

Expected: all existing and new tests pass; approval still has no inventory side effect.

- [ ] **Step 5: Commit and open one PR**

```bash
git add AgfAdmin/Controllers/IncomingInvoicesController.cs AgfAdmin.Tests/Controllers/IncomingInvoicesControllerTests.cs agf-frontend/src/components/ManualInvoiceModal.tsx
git commit -m "fix: keep supplier invoices separate from stock"
```

Do not include the frontend path if inspection proves it makes no status assumption.

### Task 2: Add the availability domain and relational test foundation

**Files:**
- Create: `AgfAdmin/Models/SupplyCommitment.cs`
- Create: `AgfAdmin/Models/SupplyCommitmentLine.cs`
- Create: `AgfAdmin/Models/StockReceipt.cs`
- Create: `AgfAdmin/Models/StockMovement.cs`
- Create: `AgfAdmin/Models/StockReservation.cs`
- Modify: `AgfAdmin/Data/AppDbContext.cs`
- Modify: `AgfAdmin/Models/ProductVariant.cs`
- Modify: `AgfAdmin/Models/Vendor.cs`
- Create: `AgfAdmin/Migrations/<timestamp>_AddInventoryAvailabilityDomain.cs`
- Modify: `AgfAdmin/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `AgfAdmin.Tests/AgfAdmin.Tests.csproj`
- Create: `AgfAdmin.Tests/Infrastructure/SqliteAppDbContextFactory.cs`
- Create: `AgfAdmin.Tests/Models/InventoryAvailabilityModelTests.cs`

**Consumes:** Task 1 guarantee that invoices are not stock writers.

**Produces:** additive schema and stable identifiers for Tasks 3–6. Legacy `Inventory`, `Shipment` and `OrderLineAllocation` remain untouched in this task.

**Interfaces:**

```csharp
public sealed class SupplyCommitment
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public string Route { get; set; } // "Warehouse" or "DirectTrade"
    public string Status { get; set; } // "Expected", "PartiallyReceived", "Received", "Cancelled", "Delivered"
    public string Origin { get; set; }
    public string? Destination { get; set; }
    public DateTime? ExpectedAt { get; set; }
    public ICollection<SupplyCommitmentLine> Lines { get; set; }
}

public sealed class SupplyCommitmentLine
{
    public int Id { get; set; }
    public int SupplyCommitmentId { get; set; }
    public int ProductVariantId { get; set; }
    public int ExpectedQuantity { get; set; }
    public string? LotNumber { get; set; }
}

public sealed class StockReceipt { public int Id { get; set; } public Guid RequestId { get; set; } }
public sealed class StockMovement { public int Id { get; set; } public string Type { get; set; } public int Quantity { get; set; } }
public sealed class StockReservation { public int Id { get; set; } public int OrderLineId { get; set; } public int SupplyCommitmentLineId { get; set; } }
```

The implementation must add the foreign keys, timestamps, source links and navigation properties needed to enforce the state model. `RequestId` has a unique database index. `StockMovement.Type` is constrained in service code to `Receipt`, `Dispatch`, `Waste`, `Correction`, `Reservation` or `ReservationRelease`; movement quantities are signed only where the type contract requires it.

- [ ] **Step 1: Write failing SQLite relational-model tests**

Add tests that assert: duplicate `StockReceipt.RequestId` violates the unique index; a `SupplyCommitmentLine` requires its parent and variant; and a `StockReservation` links exactly one order line to exactly one source line. Use a shared open SQLite in-memory connection, not EF InMemory, so relational constraints are exercised.

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~InventoryAvailabilityModelTests`

Expected: FAIL because the types and DbSets do not exist.

- [ ] **Step 3: Add models, DbSets, Fluent configuration and migration**

Register `SupplyCommitments`, `SupplyCommitmentLines`, `StockReceipts`, `StockMovements` and `StockReservations` in `AppDbContext`. Use `OnModelCreating` for required fields, indexes and delete behaviour; do not add attributes that silently cascade-delete audit records. Generate one EF migration with `dotnet ef migrations add AddInventoryAvailabilityDomain --project AgfAdmin/AgfAdmin.csproj` and inspect it before committing.

- [ ] **Step 4: Add the SQLite test provider and re-run tests**

Add `Microsoft.EntityFrameworkCore.Sqlite` version `9.0.7` to the test project. Run:

```bash
dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~InventoryAvailabilityModelTests
dotnet ef migrations has-pending-model-changes --project AgfAdmin/AgfAdmin.csproj
```

Expected: model tests pass and EF reports no pending model changes.

- [ ] **Step 5: Commit and open one PR**

```bash
git add AgfAdmin/Models AgfAdmin/Data/AppDbContext.cs AgfAdmin/Migrations AgfAdmin.Tests
git commit -m "feat: add inventory availability domain"
```

### Task 3: Create expected supply, record receipts and expose availability

**Files:**
- Create: `AgfAdmin/DTOs/SupplyCommitmentDtos.cs`
- Create: `AgfAdmin/Services/IStockAvailabilityService.cs`
- Create: `AgfAdmin/Services/StockAvailabilityService.cs`
- Create: `AgfAdmin/Controllers/SupplyCommitmentsController.cs`
- Modify: `AgfAdmin/Program.cs`
- Create: `AgfAdmin.Tests/Services/StockAvailabilityServiceTests.cs`
- Create: `AgfAdmin.Tests/Controllers/SupplyCommitmentsControllerTests.cs`

**Consumes:** Task 2 models, schema and SQLite factory.

**Produces:** warehouse supply API; no existing fast-sale/order endpoint switches to it yet.

**Interfaces:**

```csharp
Task<SupplyCommitmentDto> CreateExpectedAsync(CreateSupplyCommitmentDto request, CancellationToken cancellationToken = default);
Task<ReceiptResultDto> RecordReceiptAsync(int commitmentId, RecordSupplyReceiptDto request, CancellationToken cancellationToken = default);
Task<IReadOnlyList<VariantAvailabilityDto>> GetAvailabilityAsync(CancellationToken cancellationToken = default);
```

`CreateSupplyCommitmentDto` has `VendorId`, `Origin`, optional `ExpectedAt`, and one or more positive-quantity lines. `RecordSupplyReceiptDto` has a non-empty `Guid RequestId` and one or more positive receipt lines. `VariantAvailabilityDto` returns `ProductVariantId`, `PhysicalOnHand`, `ExpectedInbound`, `ReservedPhysical`, `ReservedExpected`, `AvailableNow` and `AvailableToPromise` as separate integers.

- [ ] **Step 1: Write failing service/controller tests**

Cover these named behaviours:

```csharp
CreateExpectedAsync_Rejects_Empty_Or_NonPositive_Lines();
CreateExpectedAsync_Produces_Expected_Availability_Without_Physical_Stock();
RecordReceiptAsync_Moves_A_Partial_Quantity_From_Expected_To_Physical();
RecordReceiptAsync_With_The_Same_RequestId_Is_Idempotent();
RecordReceiptAsync_Rejects_Quantity_Greater_Than_Unreceived_Expected();
```

The idempotency assertion must call the method twice with the same `RequestId` and prove there is one receipt/movement and one physical increment.

- [ ] **Step 2: Run focused tests to verify failure**

Run: `dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter "FullyQualifiedName~StockAvailabilityServiceTests|FullyQualifiedName~SupplyCommitmentsControllerTests"`

Expected: FAIL because service, DTOs and controller do not exist.

- [ ] **Step 3: Implement the transactional availability service**

Create all expected quantities through `SupplyCommitmentLine`; create every physical intake as a `StockReceipt` plus `StockMovement` of type `Receipt`. Use one database transaction for the idempotency lookup, remaining-quantity validation, receipt creation and movement writes. Repeated `RequestId` returns the prior result without another movement. Availability is calculated from commitments, receipts, movements and future reservation records; it is not stored as a mutable total.

- [ ] **Step 4: Implement the controller and register the service**

Expose:

```text
POST /api/supply-commitments
POST /api/supply-commitments/{id}/receipts
GET  /api/stock-availability
```

Return `400` for invalid input, `404` for unknown commitment, and `409` for an over-receipt or invalid commitment state. Do not alter `ShipmentsController` in this task.

- [ ] **Step 5: Run backend verification**

Run:

```bash
dotnet restore Software-AGF.sln
dotnet build Software-AGF.sln --no-restore --configuration Release
dotnet test Software-AGF.sln --no-build --configuration Release
dotnet ef migrations has-pending-model-changes --project AgfAdmin/AgfAdmin.csproj
```

Expected: all tests pass, build has zero errors and EF reports no pending changes.

- [ ] **Step 6: Commit and open one PR**

```bash
git add AgfAdmin/DTOs AgfAdmin/Services AgfAdmin/Controllers/SupplyCommitmentsController.cs AgfAdmin/Program.cs AgfAdmin.Tests
git commit -m "feat: record expected supply and physical receipts"
```

### Task 4: Replace negative backorders with source reservations and dispatch

**Files:**
- Modify: `AgfAdmin/DTOs/FastOrderDtos.cs`
- Modify: `AgfAdmin/DTOs/CreateOrderDto.cs`
- Modify: `AgfAdmin/Controllers/SalesController.cs`
- Modify: `AgfAdmin/Controllers/OrderController.cs`
- Modify: `AgfAdmin/Services/StockAvailabilityService.cs`
- Create: `AgfAdmin.Tests/Controllers/SalesControllerAvailabilityTests.cs`
- Create: `AgfAdmin.Tests/Controllers/OrderControllerAvailabilityTests.cs`

**Consumes:** Task 3 availability service and `StockReservation` records.

**Produces:** all new warehouse sales reserve explicit physical or expected supply; no new `Inventory` row with location `Backorder` is created.

**Interfaces:**

Extend each new sales line DTO with exactly one source selection:

```csharp
public string FulfilmentSource { get; set; } // "Physical" or "Expected"
public int? SupplyCommitmentLineId { get; set; }
```

Physical lines leave `SupplyCommitmentLineId` null and are allocated FIFO from eligible receipt movements/lots. Expected lines require a matching expected supply-line id. Add `POST /api/orders/{id}/dispatch`; dispatch changes a confirmed reservation into the appropriate stock movement. Add `POST /api/orders/{id}/cancel` rather than deleting an order record.

- [ ] **Step 1: Write failing reservation/dispatch tests**

Cover:

```csharp
ConfirmOrder_Reserves_Physical_Stock_Without_Creating_Negative_Inventory();
ConfirmOrder_Reserves_Eligible_Expected_Supply_And_Leaves_Physical_Zero();
ConfirmOrder_Rejects_Quantity_Above_AvailableToPromise();
CancelOrder_Releases_Each_Reservation_Once();
DispatchOrder_Writes_A_Dispatch_Movement_And_Prevents_Second_Dispatch();
```

Also pin that `TargetShipmentId` is no longer accepted as a hidden alternate allocation path.

- [ ] **Step 2: Run focused tests to verify failure**

Run: `dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter "FullyQualifiedName~SalesControllerAvailabilityTests|FullyQualifiedName~OrderControllerAvailabilityTests"`

Expected: FAIL because both controllers still mutate `Inventory` and create ghost backorders.

- [ ] **Step 3: Route both sales entry points through reservations**

Make `SalesController.ConfirmOrder` and `OrderController.CreateOrder` call the same service operation. It validates product/source/quantity inside a transaction, writes `StockReservation` records and leaves physical quantity untouched until dispatch. Remove the negative `Inventory`/`Backorder` branch, its ghost-resolution dependencies and the restoration loop in `DeleteOrder`; replace deletion with the explicit cancellable order state. Keep terminal invoice rules strict: an invoice may only be generated for a dispatched order.

- [ ] **Step 4: Run regression tests and scan for unsafe ghost creation**

Run:

```bash
dotnet test Software-AGF.sln --configuration Release
rg -n 'Backorder|Quantity = -|ghost' AgfAdmin -g '*.cs'
```

Expected: all tests pass; no production path creates a negative stock row. Any remaining references are removed or are explanatory migration comments with no execution path.

- [ ] **Step 5: Commit and open one PR**

```bash
git add AgfAdmin/DTOs AgfAdmin/Controllers/SalesController.cs AgfAdmin/Controllers/OrderController.cs AgfAdmin/Services/StockAvailabilityService.cs AgfAdmin.Tests
git commit -m "feat: reserve stock explicitly for sales orders"
```

### Task 5: Cut over the desktop UI to expected, received and reserved availability

**Files:**
- Modify: `agf-frontend/src/services/inventoryService.ts`
- Create: `agf-frontend/src/services/supplyCommitmentService.ts`
- Modify: `agf-frontend/src/app/(admin)/inventory/page.tsx`
- Create: `agf-frontend/src/components/CreateExpectedSupplyModal.tsx`
- Replace: `agf-frontend/src/components/ReceiveShipmentModal.tsx`
- Modify: `agf-frontend/src/components/AddOrderModal.tsx`
- Modify: `agf-frontend/src/app/(admin)/orders/page.tsx`
- Modify: `agf-frontend/src/app/sales/[sellerId]/page.tsx`
- Modify: `agf-frontend/src/i18n/nl.json`
- Modify: `agf-frontend/src/i18n/en.json`

**Consumes:** Tasks 3–4 API contracts.

**Produces:** a desktop workflow that exposes the same availability semantics as the backend; the old shipment/ghost UI is removed from normal operation.

- [ ] **Step 1: Add failing service/type tests where the frontend project supports them, otherwise record API-contract checks**

If no frontend test runner exists, add no test dependency solely for this UI task. Instead add a checked-in API contract fixture or use backend controller tests for payload shape, then capture the manual acceptance sequence below in the PR description.

- [ ] **Step 2: Implement separate availability totals and fast actions**

Replace the single summed quantity/negative-debt presentation with per-variant cards or rows for **Physical**, **Expected**, **Reserved** and **Available now**. Add two clear actions after the quantity/supplier/unit form is completed:

```text
+ Expected  -> POST /api/supply-commitments
+ Received  -> create/choose supply commitment, then POST its receipt endpoint
```

Do not label expected quantity as warehouse stock. Remove all user-facing text and controls referring to “Ghost Sweeper” or “Backorder Ghosts”.

- [ ] **Step 3: Implement sale source selection and lifecycle actions**

At order/fast-sale entry, let the user select **Own warehouse** or **Expected supplier quantity**. Show the latter as *onderweg* and require a source commitment line. Provide explicit cancel and dispatch actions; do not render a destructive delete as a stock correction.

- [ ] **Step 4: Validate UI build and API flow**

Run:

```bash
cd agf-frontend && npm ci && npm run lint && npm run build
```

Then manually validate against a disposable local database:

1. create three expected pallets;
2. reserve one expected pallet;
3. receive all three;
4. confirm physical is three, reserved is one and available-now is two;
5. cancel the reservation and confirm available-now returns to three.

- [ ] **Step 5: Commit and open one PR**

```bash
git add agf-frontend
git commit -m "feat: show expected and physical stock availability"
```

### Task 6: Add direct supplier-to-customer trade

**Files:**
- Modify: `AgfAdmin/Models/SupplyCommitment.cs`
- Modify: `AgfAdmin/Models/Order.cs`
- Modify: `AgfAdmin/Data/AppDbContext.cs`
- Create: `AgfAdmin/Migrations/<timestamp>_AddDirectTradeCommitments.cs`
- Modify: `AgfAdmin/DTOs/FastOrderDtos.cs`
- Modify: `AgfAdmin/DTOs/CreateOrderDto.cs`
- Modify: `AgfAdmin/Services/StockAvailabilityService.cs`
- Modify: `AgfAdmin/Controllers/SupplyCommitmentsController.cs`
- Modify: `AgfAdmin/Controllers/SalesController.cs`
- Modify: `AgfAdmin/Controllers/OrderController.cs`
- Create: `AgfAdmin.Tests/Controllers/DirectTradeTests.cs`
- Modify: `agf-frontend/src/components/AddOrderModal.tsx`
- Modify: `agf-frontend/src/app/sales/[sellerId]/page.tsx`
- Modify: `agf-frontend/src/app/(admin)/orders/page.tsx`

**Consumes:** Tasks 3–5 working warehouse availability and reservations.

**Produces:** direct trade linked to supplier and customer without physical warehouse stock.

**Interfaces:**

For `FulfilmentSource == "DirectTrade"`, the request requires `SupplyCommitmentLineId`, supplier commitment route `DirectTrade`, and a customer destination. `Order` stores a direct-trade route/reference; the supply commitment stores origin/ship-from and destination. No `StockReceipt` or physical `StockMovement` is created by this path.

- [ ] **Step 1: Write failing direct-trade tests**

```csharp
CreateDirectTrade_Requires_Supplier_Source_And_Customer_Destination();
CreateDirectTrade_Reserves_The_Supply_Line_Without_Physical_Stock();
CreateDirectTrade_Cannot_Reserve_The_Same_Expected_Quantity_Twice();
CompleteDirectTrade_Does_Not_Create_A_Warehouse_Dispatch_Or_Receipt();
```

- [ ] **Step 2: Run the focused tests to verify failure**

Run: `dotnet test AgfAdmin.Tests/AgfAdmin.Tests.csproj --filter FullyQualifiedName~DirectTradeTests`

Expected: FAIL because no direct-trade source/lifecycle exists.

- [ ] **Step 3: Implement direct-trade lifecycle and UI route choice**

Add route/destination persistence and guarded transitions from `Expected` to `Delivered` or `Cancelled`. Reuse the existing reservation mechanism; do not create a warehouse receipt/dispatch. In the UI, provide **Direct from supplier** as a third fulfilment choice only after supplier and customer/destination are selected.

- [ ] **Step 4: Run full quality verification and review database reset separately**

Run:

```bash
dotnet restore Software-AGF.sln
dotnet build Software-AGF.sln --no-restore --configuration Release
dotnet test Software-AGF.sln --no-build --configuration Release
cd agf-frontend && npm ci && npm run lint && npm run build
```

Expected: all backend tests, lint and production frontend build pass. Before any local test database reset, show the exact target database/volume and obtain explicit user confirmation; never perform it from startup code or a migration.

- [ ] **Step 5: Commit and open one PR**

```bash
git add AgfAdmin AgfAdmin.Tests agf-frontend
git commit -m "feat: support direct supplier-to-customer trade"
```

## Self-review

- **Spec coverage:** expected, physical, reserved and direct trade each have a data model, API/UI flow and acceptance test task. Invoice separation is Task 1; idempotency, partial receipt, non-negative availability, release and direct-trade no-physical-stock cases are in Review Focus and covered by Tasks 3, 4 and 6.
- **Step scan:** each task produces one reviewable PR boundary and its own red/green verification loop. No task asks an implementer to infer a status meaning or stock source.
- **Type consistency:** Tasks 3–6 use the `SupplyCommitment`, `SupplyCommitmentLine`, `StockReceipt`, `StockMovement` and `StockReservation` names introduced in Task 2. `FulfilmentSource` values are consistently `Physical`, `Expected` and `DirectTrade`.
- **Review Focus:** every listed operational failure has a named test in its owning task.
- **Proportion:** this is a rollout plan, not code. It fixes interfaces, state values, tests and PR boundaries while leaving method bodies to implementation.

## Execution handoff

Start with **Task 1 only**. It is an isolated data-safety fix and already has an agreed product decision. After it is merged, begin Task 2 on a fresh branch. Use the existing workflow: Claude Code prepares a no-edit implementation plan, Hasan approves it, then Claude opens a focused PR; no PR is merged automatically.

