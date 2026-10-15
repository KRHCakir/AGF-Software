# Inventory availability & trade-flow design

**Status:** approved product design, pending implementation plan  
**Date:** 2026-10-15  
**Scope:** desktop MVP inventory availability, purchase intake, warehouse receipt, reservation and direct trade

## Why this design exists

AGF wholesalers need to trade quickly. A buyer may confirm three pallets from a supplier and sell part of that commercial availability before the goods physically arrive at its warehouse. Other wholesalers only register the goods when they arrive.

The application must support both working styles without ever presenting expected or direct-trade goods as physical warehouse stock.

## Core decision

A supplier invoice is an administrative/financial document. It does **not** create or change physical stock.

A separate purchase/intake record represents the commercial supply commitment. A separate receipt represents the physical arrival of goods. A sales order creates a reservation. These are distinct operations with visible statuses and audit history.

## Availability states

| State | Meaning | Can be sold? | Counted as physical warehouse stock? |
| --- | --- | --- | --- |
| Expected | Purchase is confirmed or goods are inbound | Yes, as *underway/expected*, when eligible | No |
| Physical available | Goods have been received into the warehouse | Yes | Yes |
| Reserved | Quantity is allocated to a customer sale | Not again | Only when it originated from a warehouse receipt |
| Direct trade | Supplier delivers from origin directly to the customer | Yes, only through the linked sale | No |
| Completed/cancelled | Supply or sale is fulfilled/cancelled | No | No |

The UI must always distinguish:

- **Physical on hand**: goods actually in the selected warehouse;
- **Expected/inbound**: confirmed supplier quantity not yet received;
- **Reserved**: quantity already promised to a customer;
- **Available now**: physical on hand minus warehouse reservations;
- **Available to promise**: eligible physical plus eligible expected quantity, minus all reservations.

Expected quantity must never be added silently to the physical-on-hand total.

## Fast desktop workflow

Frequent actions must be usable in seconds.

On a product/variant line, the office user enters quantity, unit and supplier, then selects one clear one-click action:

- **+ Expected**: create confirmed inbound availability; show it as underway, not warehouse stock.
- **+ Received**: register a physical receipt and immediately make it available in the warehouse.

The interface should prefill or remember the last commonly used supplier, stock unit and packaging where appropriate. It must still show the selected unit and status before saving.

When creating a sale, the user sees physical, expected and reserved quantities separately. A sale can be supplied by:

1. **Own warehouse stock** — reserve an eligible physical quantity;
2. **Expected supplier quantity** — reserve a confirmed inbound quantity and label the sale as underway;
3. **Direct supplier delivery** — link customer, supplier, origin/ship-from, delivery destination and quantity. This never creates Dutch warehouse stock.

No user setting creates separate truths about what “stock” means. The workflow is flexible; the operational facts remain explicit.

## State transitions

### Purchase and warehouse receipt

1. Office creates a supply commitment as **Expected**.
2. A customer sale may reserve eligible expected quantity.
3. When the goods arrive at a warehouse, an explicit receipt changes the received quantity to **Physical available**.
4. Receipt consumes only the matching expected quantity. A receipt cannot be applied twice.
5. Partial receipt and remaining expected quantity are supported.
6. Cancellation/reduction releases unreserved expected quantity; a reserved quantity requires an explicit correction or sales decision first.

### Warehouse sale

1. Create sales order.
2. Reserve a specific available quantity.
3. Dispatch/fulfilment reduces the reserved physical quantity.
4. Cancelling before dispatch releases the reservation.
5. Corrections use a new compensating movement with a reason; history is never silently rewritten.

### Direct trade / dropshipment

1. Create or link a confirmed supplier commitment.
2. Create a customer sale with direct-delivery route.
3. Reserve/commit that supplier quantity exclusively to the sale.
4. Record delivery/transport milestones later; complete once delivered.
5. No warehouse receipt, physical-stock movement or Dutch warehouse balance is created unless goods actually enter a warehouse.

## Integrity rules

- A quantity cannot be reserved more than once.
- The system never creates negative physical stock automatically.
- A transaction that changes availability is idempotent: retrying the same receipt, reservation or dispatch cannot apply it twice.
- A direct trade must identify both supplier/source and customer/destination.
- Quantity/unit conversions are recorded with the transaction or lot, not recalculated from later product settings.
- Every receipt, reservation, release, dispatch, cancellation and correction records timestamp, signed-in user and reason where relevant.
- Existing invoice approval/rejection rules remain administrative status rules. They do not alter stock or availability.

## Data-model direction (not implementation)

The current direct-edit inventory balance is insufficient for this design. The implementation will introduce an append-only availability/stock-movement ledger and explicit records for supply commitments, receipts, sales reservations and direct-trade commitments.

Every relevant inbound flow carries a lot/party identifier from the start where known. This preserves future FIFO, traceability/recall and per-lot margin reporting without making those P2 features part of this first delivery.

The exact entity/table names are deliberately left to the implementation plan; the state semantics and invariants above are mandatory.

## MVP boundary

Included in the next operational-core work:

- expected vs physical vs reserved visibility;
- supplier commitment, receipt and sales reservation flows;
- direct supplier-to-customer trade;
- audit-safe corrections;
- tests for availability and double-application protections.

Deferred:

- FIFO suggestions and recall reporting;
- pallet/SSCC, locations and quarantine;
- automated accounting sync, VAT filing or general ledger;
- full offline synchronisation;
- AI invoice extraction. AI may later create a draft for review, but it must follow this same supply/receipt model.

## Acceptance examples

1. **Three expected pallets:** user confirms three pallets from supplier B. Physical is 0; expected is 3; available-to-promise is 3.
2. **Sell one while inbound:** user sells one pallet to customer C. Expected remains 3; reserved is 1; available-to-promise is 2. Physical stays 0.
3. **Receive all three:** user records receipt of three pallets. Physical is 3; expected is 0; one pallet remains reserved; available-now is 2.
4. **Direct international trade:** supplier in Kenya sends a pallet directly to a customer in Turkey. The system records the customer/supplier linkage and commitment but physical Netherlands stock remains unchanged.
5. **Correction:** user records one pallet too many. The correction is a visible reversing movement with reason; historical receipt is not silently edited.
