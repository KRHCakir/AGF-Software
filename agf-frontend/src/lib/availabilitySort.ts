import type { QualityBreakdown } from '../types/availability';
import type { DirectTradeRow, WarehouseRow } from './availability';
import type { SortColumns } from './listSort';

// Sorteren in Beschikbaarheid (feedback 22 november): elke kolom van de drie weergaven (verkoop, compact, alle kolommen) en van de
// klantgebonden aanvoer. Aantallen sorteren op het aantal colli/stuks; de standaard blijft de volgorde van de server (op product).

const total = (breakdown: QualityBreakdown) => breakdown.approved.quantity + breakdown.quarantine.quantity + breakdown.blocked.quantity + breakdown.rejected.quantity;
const product = (row: { variant: { productName: string; variantName: string } }) => `${row.variant.productName} ${row.variant.variantName}`.trim();

export const WAREHOUSE_COLUMNS: SortColumns<WarehouseRow> = {
    product: { kind: 'text', value: product },
    unit: { kind: 'text', value: (row) => row.supply.unit },
    // Verkoopweergave
    expected: { kind: 'number', value: (row) => row.supply.sales?.expected ?? null },
    stock: { kind: 'number', value: (row) => row.supply.sales?.stock ?? row.supply.availableBeforeReservations.quantity },
    sold: { kind: 'number', value: (row) => row.supply.sales?.sold ?? null },
    free: { kind: 'number', value: (row) => row.supply.sales?.free ?? null },
    // Compact en alle kolommen
    onHand: { kind: 'number', value: (row) => total(row.supply.warehouse) },
    availableBefore: { kind: 'number', value: (row) => row.supply.availableBeforeReservations.quantity },
    reserved: { kind: 'number', value: (row) => row.supply.reserved.quantity },
    availableAfter: { kind: 'number', value: (row) => row.supply.availableAfterReservations.quantity },
    reservedOnHold: { kind: 'number', value: (row) => row.supply.reservedOnHold.quantity },
    quarantine: { kind: 'number', value: (row) => row.supply.warehouse.quarantine.quantity },
    blocked: { kind: 'number', value: (row) => row.supply.warehouse.blocked.quantity },
    rejected: { kind: 'number', value: (row) => row.supply.warehouse.rejected.quantity },
    outside: { kind: 'number', value: (row) => total(row.supply.receivedOutsideWarehouse) },
    expectedOnPallets: { kind: 'number', value: (row) => total(row.supply.expectedOnPallets) },
    inTransit: { kind: 'number', value: (row) => total(row.supply.inTransit) },
    expectedUnregistered: { kind: 'number', value: (row) => row.supply.expectedUnregistered.quantity },
    signals: { kind: 'number', value: (row) => row.supply.warnings.length },
};

// Per weergave alleen de kolommen die er staan (en dus een eigen onthouden keuze per weergave).
export const SALES_VIEW_KEYS = ['product', 'unit', 'expected', 'stock', 'sold', 'free', 'signals'] as const;
export const COMPACT_VIEW_KEYS = ['product', 'unit', 'onHand', 'reserved', 'availableAfter', 'quarantine', 'expectedUnregistered', 'signals'] as const;
export const DETAILED_VIEW_KEYS = ['product', 'unit', 'availableBefore', 'reserved', 'availableAfter', 'reservedOnHold', 'onHand', 'quarantine', 'blocked', 'rejected',
    'outside', 'expectedOnPallets', 'inTransit', 'expectedUnregistered', 'signals'] as const;

export const DIRECT_TRADE_COLUMNS: SortColumns<DirectTradeRow> = {
    customer: { kind: 'text', value: (row) => row.supply.customerName },
    product: { kind: 'text', value: product },
    unit: { kind: 'text', value: (row) => row.supply.unit },
    outside: { kind: 'number', value: (row) => total(row.supply.receivedOutsideWarehouse) },
    expectedOnPallets: { kind: 'number', value: (row) => total(row.supply.expectedOnPallets) },
    inTransit: { kind: 'number', value: (row) => total(row.supply.inTransit) },
    expectedUnregistered: { kind: 'number', value: (row) => row.supply.expectedUnregistered.quantity },
    signals: { kind: 'number', value: (row) => row.supply.warnings.length },
};
export const DIRECT_TRADE_KEYS = Object.keys(DIRECT_TRADE_COLUMNS);
