import { isNumber, isString } from './guards';

// Kosten verdelen over partijen (api/purchase-invoices/{id}/allocations): zie AgfAdmin/DTOs/PartijCostDtos.cs. Bedragen excl. btw, euro.
export interface LineAllocation {
    partijId: number;
    number: string;
    partijKind: 'Purchase' | 'Sales';
    amount: number;
    // Wat het is en de basis voor verdelen (ontvangen bij inkoop, besteld bij verkoop); ontbreekt bij een oudere server.
    party?: string | null;
    products?: string | null;
    quantity?: number;
    unit?: string | null;
    netKg?: number;
}

export interface AllocationHistory {
    occurredAt: string;
    type: 'Allocated' | 'Changed' | 'Removed';
    partijId: number;
    number: string | null;
    oldAmount: number | null;
    newAmount: number | null;
    actorReference: string;
}

export interface InvoiceLineAllocation {
    lineId: number;
    description: string | null;
    kind: string;
    lineTotal: number;
    allocated: number;
    general: number;
    version: number;
    allocations: LineAllocation[];
    history: AllocationHistory[];
}

export interface InvoiceAllocations {
    invoiceId: number;
    currency: string;
    canAllocate: boolean;
    lines: InvoiceLineAllocation[];
}

export interface SaveLineAllocationRequest {
    requestId: string;
    expectedVersion: number;
    allocations: { partijId: number; amount: number }[];
}

function isLine(value: unknown): value is InvoiceLineAllocation {
    if (typeof value !== 'object' || value === null) return false;
    const record = value as Record<string, unknown>;
    return isNumber(record.lineId) && isNumber(record.lineTotal) && isNumber(record.version) && Array.isArray(record.allocations) && Array.isArray(record.history);
}

export function isInvoiceAllocations(value: unknown): value is InvoiceAllocations {
    if (typeof value !== 'object' || value === null) return false;
    const record = value as Record<string, unknown>;
    return isNumber(record.invoiceId) && isString(record.currency) && typeof record.canAllocate === 'boolean' && Array.isArray(record.lines) && record.lines.every(isLine);
}
