import { isNumber, isString } from './guards';

// Kosten verdelen over partijen (api/purchase-invoices/{id}/allocations): zie AgfAdmin/DTOs/PartijCostDtos.cs. Bedragen op partijen
// excl. btw, altijd in euro; een factuur in een andere valuta wordt omgerekend met haar ECB-koers.
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
    // Regelbedrag in de valuta van de factuur.
    lineTotal: number;
    // Hetzelfde in euro (de grens voor de verdeling); null bij een andere valuta zonder koers. Ontbreekt bij een oudere server (dan euro).
    lineTotalEur?: number | null;
    allocated: number;
    // Niet verdeeld, in euro: algemene kosten; null zonder koers.
    general: number | null;
    version: number;
    allocations: LineAllocation[];
    history: AllocationHistory[];
}

export interface InvoiceAllocations {
    invoiceId: number;
    currency: string;
    // ECB-koers bij een factuur in een andere valuta: eenheden valuta per 1 euro, publicatiedag (jjjj-mm-dd) en bron.
    exchangeRate?: number | null;
    exchangeRateDate?: string | null;
    exchangeRateSource?: string | null;
    // Er staan kosten op partijen: de koers ligt vast.
    rateLocked?: boolean;
    canAllocate: boolean;
    lines: InvoiceLineAllocation[];
}

// Een ECB-koers om te tonen (api/exchange-rates).
export interface ExchangeRate {
    currency: string;
    unitsPerEuro: number;
    rateDate: string;
    forDate: string;
    source: string;
}

// Het eurobedrag van een regel: de grens voor de verdeling (bij EUR het regelbedrag zelf).
export function lineEuro(line: InvoiceLineAllocation, currency: string): number | null {
    if (line.lineTotalEur !== undefined) return line.lineTotalEur;
    return currency.trim().toUpperCase() === 'EUR' ? line.lineTotal : null;
}

export function isExchangeRate(value: unknown): value is ExchangeRate {
    if (typeof value !== 'object' || value === null) return false;
    const record = value as Record<string, unknown>;
    return isString(record.currency) && isNumber(record.unitsPerEuro) && isString(record.rateDate) && isString(record.source);
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
