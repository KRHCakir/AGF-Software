# AGF pallet, unit, quality and ATP design addendum

**Status:** approved conversational design; pending written-spec review  
**Date:** 2026-10-16  
**Scope:** clarifies and extends `2026-10-15-inventory-availability-design.md` before implementation Task 3 begins.

## Purpose

AGF wholesalers must register real commercial and physical facts without software-created weight assumptions. This addendum covers quantities, size, pallets, quality, traceability, transit and Available to Promise (ATP).

It supersedes the earlier plan's assumptions that availability quantities are whole integers without a unit, and that pallet/quality/transit are deferred. No Task 3 availability API or UI work may start until this addendum is reviewed and a replacement implementation plan is approved.

## Product variants, sizes and quantities

A product variant is a distinct sellable and traceable combination of product, variant, size, packaging and other configured properties. For example, avocado Hass size 6 and size 8 are different variants.

The application must never assume a weight from a product size. A variant stores no default “kg per box” rule.

Each inbound or direct-trade source line records the actual facts entered by the user:

- product variant;
- explicit package quantity and explicit package unit;
- actual total net weight in kg;
- known or provisional lot/party reference; a final lot is mandatory when physical content is recorded;
- supplier/source and route;
- optional pallet quantity.

Package units are explicit, not free-text ambiguity. The initial controlled values are **box**, **colli**, **tray**, **piece** and **dozen**. A box and a dozen are not interchangeable. Net weight is a decimal value; quantities used in availability must support decimal precision.

The system can calculate a conversion for the recorded source line only from user-entered actual quantities. It never infers the contents, count or weight of a box or pallet. This line-level conversion is an immutable historical snapshot.

## Pallet and contents model

A pallet is a logistics container with one or more atomic content lines. “Full pallet” is not a data rule: a single-product pallet can contain multiple lots and therefore multiple content lines.

### Pallet

A pallet has:

- required unique scanable pallet reference;
- optional valid GS1 SSCC;
- pallet type;
- optional measured tare kg;
- optional measured gross kg;
- calculated net kg, derived from its content lines;
- logistics status;
- required location context;
- optional precise warehouse location, carrier, transport document and route.

The application must not label an internally generated reference as an SSCC. It is an SSCC only when the user scans or enters a genuine valid GS1 SSCC.

### Content line

A content line is the smallest physical and stock-traceable unit. It contains:

- pallet reference;
- product variant;
- exactly one lot/party;
- exactly one source supply-commitment line;
- package quantity and explicit package unit;
- actual net kg;
- quality status and quality metadata.

A pallet relates to many source lines through its content lines, and a source line may appear on many pallets. A single content line must not blend multiple lots or sources. If two lots of the same variant are on one pallet, they are two content lines.

Inventory, allocation, recall, quality decisions and later invoicing/dispatch trace to the content line and its lot; a pallet alone is never the stock owner.

## Weights and validation

- Pallet net kg is always the sum of content-line net kg.
- Gross kg and tare kg are independently captured measured values. Neither is guessed from the other values.
- If a sticker declares a total net kg, it is stored as a separate declared/measured value and compared with calculated line net kg using a configured tolerance.
- If gross kg and tare kg are both known, gross kg must be at least net kg plus tare kg.
- Positive package quantities and positive net kg are mandatory for every content line.
- A mismatch outside tolerance blocks finalisation until the user corrects it or records an authorised reason.

## Logistics, location and quality

Pallet logistics status and content quality status are separate.

Pallet logistics statuses are: **Expected**, **InTransit**, **Received**, **Dispatched**, **Delivered**, **Cancelled**.

Every pallet has exactly one location context: **Supplier**, **Warehouse**, **Crossdock**, **InTransit**, **Customer** or **External**. A warehouse bin/cold-room code is required only for goods physically in the organisation's warehouse.

Content quality statuses are: **Quarantine**, **Approved**, **Blocked**, **Rejected**.

- **Quarantine** means awaiting inspection.
- **Blocked** is an active hold, for example a claim or recall, and must be explicitly lifted.
- **Rejected** means the quantity cannot be sold through this stock path.
- Partially accepted content is represented by splitting the original quantity into atomic content lines with their own statuses. The system does not use an ambiguous “partly approved” quantity.

Every change of logistics status, location, quantity, weight, quality status or quality split creates an immutable audit event with actor, timestamp, old value, new value and reason. Authentication will supply the actor once it is available; until then, an explicit actor/reference field must be supported for operational auditability.

## ATP and trade rules

The interface and API expose separate quantities. They must never be silently combined:

| Category | Meaning | Eligible for sale/reservation? | Physical warehouse stock? |
| --- | --- | ---: | ---: |
| Physical available | Received, approved goods at a saleable own location | Yes | Yes |
| Transit | Confirmed goods physically travelling, not yet available to pick | Yes, with explicit source, warning and delivery date | No |
| Expected | Confirmed source not yet shipped/received | Yes, with explicit source, warning and delivery date | No |
| Reserved | Exact quantity linked to a customer order | Not again | Depends on source |
| Quarantine / Blocked / Rejected | Quality-controlled non-saleable quantity | No | No |
| Direct trade | Customer-linked supplier/origin delivery | Only to that linked customer | No |

ATP is a promise calculation, not a physical-stock total. It considers eligible physical, transit and expected source lines minus exact existing reservations. Direct-trade quantities are customer-specific commitments and are never general warehouse availability.

A sale from expected or transit supply must require:

- a selected source line;
- a visible “not physically available” warning;
- a promised delivery date;
- reservation at content-line/lot/source level once the pallet/content exists.

Quarantine, blocked and rejected quantities cannot be selected for a sale or reservation.

## Operational flows

### Own warehouse

1. Create expected supply with source-line quantities, package units and net kg; record a lot when known.
2. On arrival, create/scan a pallet and atomic content lines with a mandatory final lot, linked to those source lines.
3. Record inspection and quality outcome on each content line.
4. Only approved content at a saleable own location becomes physical available stock.
5. Dispatch consumes an explicit reservation; corrections add compensating events rather than rewrite history.

### Transit and expected sale

1. Create a customer order with the exact expected or transit source line.
2. Display warning, source, route and promised delivery date.
3. Reserve the exact available quantity once.
4. On receipt, the reservation continues against the matching content/lot; it must not be reassigned silently.

### Direct trade

1. Create a direct-trade commitment with supplier/origin and customer/destination.
2. Record expected and, where known, pallet/content/lot facts with location context Supplier, InTransit, Crossdock, Customer or External.
3. Reserve only for the linked customer.
4. Never create a Dutch warehouse receipt or physical balance unless goods actually enter the warehouse.

## Data and migration implications

The existing availability-domain schema was intentionally additive and is not yet used by live availability flows. Before Task 3:

1. create a focused corrective schema PR;
2. change availability quantities from whole integers to decimal precision;
3. add explicit unit and actual-net-weight fields to source lines;
4. add pallet and content-line entities, including source-line, lot and quality relationships;
5. add append-only audit/status-event records or an equivalent auditable event model;
6. preserve existing legacy data and never automate a reset.

This is a controlled new migration. It must be reviewed for non-destructive behaviour, restrictive foreign keys and explicit indexes. Existing test data may only be reset through a separately approved manual action.

## Required acceptance tests

- A size-6 and size-8 variant cannot share stock or reservation quantities.
- A content line cannot be saved without lot, explicit package unit, positive package quantity or positive net kg.
- One pallet can carry multiple lots of the same variant as separate content lines.
- One supply line can be split over multiple pallets.
- Net pallet kg equals the sum of its content lines; an out-of-tolerance measured total requires resolution.
- Quarantine, blocked and rejected content is absent from ATP and cannot be reserved.
- A transit or expected reservation requires a source line and promised delivery date, and does not change physical warehouse quantity.
- A direct trade stays outside Dutch physical stock.
- A partial quality result produces separate traceable approved and non-saleable quantities.
- Repeating a receipt, status transition or reservation request is idempotent and produces no duplicate stock effect.
- Audits show who/when/why for every status, location, quality or weight change.

## Out of scope

- AI and local-model integration;
- automatic weight guesses or default size-to-weight conversions;
- automated accounting, VAT filing and general ledger;
- full offline synchronisation;
- full GS1 validation or SSCC issuance workflow beyond safe storage/scanning;
- FIFO suggestion, recall-report UI and margin reports (the required data remains traceable for later work).
