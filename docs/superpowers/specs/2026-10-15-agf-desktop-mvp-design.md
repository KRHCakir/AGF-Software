# AGF Desktop MVP — Technical Product Design

**Status:** approved discovery design  
**Date:** 2026-10-15  
**Scope:** desktop-first AGF wholesale operations platform

## Product boundary

AGF Software is sold to multiple wholesale customers. It is operational software, not a personal tool or a full accounting product.

Each customer receives a separately managed cloud API and PostgreSQL database, its own users/settings/data, and a desktop-first client. The client remains online-first. A desktop wrapper can be added later without duplicating UI or backend logic.

Not in MVP: VAT returns, general ledger, tax advice, customs declarations, full offline operation, pallet/SSCC management, or autonomous AI decisions.

## Users and security

| Role | Access |
| --- | --- |
| Administrator | Users, company settings and all operational data |
| Office | Products, parties, customers, suppliers, purchases, sales, invoices, corrections and reports |
| Sales | Product/stock search and fast sale/order creation only |

Every approval, correction and status transition records the signed-in user and timestamp.

Security baseline:
- all business endpoints require authentication and role-based authorization;
- no credentials/keys/production URLs in Git;
- environment-based configuration for CORS, external URLs and connection strings;
- validated file uploads;
- per-customer backup/restore;
- no startup database deletion in any customer environment.

## Inventory, units and auditability

Each ProductVariant has one primary stock unit: kg or colli. It can define conversions such as 1 colli = 5 kg and 1 pallet = 80 colli. A conversion is snapshotted when used on a lot/transaction so history cannot change when configuration changes.

Stock uses an append-only StockMovement ledger, never a directly editable balance. Movements include receipt, reservation, reservation release, dispatch, correction, waste, return, transfer (future), and direct-trade commitment. Errors are corrected with compensating movements.

A stock view distinguishes physical on-hand, confirmed inbound, reserved, available-to-promise, and in-transit/direct-trade quantities. The application never automatically creates negative physical stock.

## Parties, lots and trade flows

Every inbound purchase can create a lot/partijnummer from day one. A lot stores supplier/origin, variant, expected/received quantity, unit, party number, status/dates, optional quality/expiry and conversion snapshot.

### Warehouse flow

1. Office confirms a purchase.
2. Receipt creates a lot and physical-stock movement.
3. A sale reserves available stock.
4. Dispatch allocates the sale to lots and creates stock-out movements.
5. The invoice is generated from the final sale/order.

### Direct international trade / dropshipment

This is an MVP requirement. A wholesaler can sell goods from an origin supplier directly to an overseas end customer without goods entering its Dutch warehouse.

A direct-trade line links the customer sales order, supplier purchase order, origin/ship-from, delivery destination, variant, quantity, lot where known, transport mode and milestones. It is not counted as physical warehouse stock, but confirmed commitment/allocation prevents promising the same commercial quantity twice.

## Documents and finance boundary

Customer invoice PDFs need unique, non-reusable invoice numbers. Sales/purchase records retain line amount, VAT rate and ISO currency code. The initial UI may focus on EUR; currency support is retained for international trade.

Bookkeeping integration is export/synchronisation only. It never becomes an in-app ledger or VAT filing system. A direct API integration waits until the target package is chosen.

## AI assistance

AI is phase 1.5, after the manual flow is correct:

1. User uploads/forwards supplier invoice.
2. AI extracts a draft with supplier, number, dates, lines, quantities, prices and totals.
3. Office user reviews, edits, approves or rejects.
4. Only approval produces purchase/receipt and stock effects.
5. Low-confidence fields are clearly flagged.

AI never silently finalises a purchase, changes stock or issues a financial document.

## Quality gates

Every customer-facing PR must pass backend build, frontend lint/build, relevant automated tests, secret scan and authorization checks. Stock/money/approval changes require tests.

Every flow must pass:
- **Speed:** frequent sale completes in seconds.
- **Correction:** mistakes are repairable without corrupting history.
- **Units:** kg, colli and pallet conversions remain correct.

## Delivery phases

### P0 — Stabilise and secure prototype
- Remove destructive database startup behaviour.
- Rotate/externalise committed credentials.
- Fix/verify backend and frontend builds and routes.
- Add authentication, roles and audit identity.
- Remove production demo data and company fallbacks.

### P1 — Desktop operational core
- Stock ledger, lots, unit conversions and transactional allocations.
- Products/variants, customers, suppliers, purchase receipt, fast sale, reservation, dispatch and invoice PDF.
- Direct trade/dropship linkage.
- Unique invoice numbering and standard statuses.

### P1.5 — AI purchase intake
- Document-to-draft extraction, human approval, confidence and audit trail.

### P2 — Traceability
- FIFO suggestions, lot allocation views, recall/history and margin reporting.

### P3 — Warehouse scale/integrations
- Pallet/SSCC, mixed pallets, locations, quarantine, accounting sync and justified offline cache/drafts.

## Decisions still needed before related work

- accounting package(s) for export/sync;
- email/OCR intake provider and retention;
- invoice numbering convention;
- VAT configuration per customer/product;
- trade-document templates and transport milestones.
