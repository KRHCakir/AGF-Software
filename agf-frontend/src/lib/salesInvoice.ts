import type { SalesInvoice, SalesInvoiceListItem, SalesInvoiceStatus } from '../types/salesInvoice';

// Verkoopfactuur 1: kleine regels voor de schermen (zonder React, getest met node:test).

// Sorteervelden van de lijst: precies de toegestane velden van de server (AgfAdmin/Services/ListSortFields.cs, SalesInvoices).
export const SALES_INVOICE_SORT_KEYS = ['number', 'customer', 'invoiceDate', 'deliveryDate', 'dueDate', 'lots', 'status', 'net', 'gross'] as const;

export const SALES_INVOICE_STATUS_FILTERS = ['all', 'open', 'Final', 'Discarded'] as const;

export const STATUS_LABEL_KEYS: Record<SalesInvoiceStatus, string> = {
    Concept: 'si_status_concept',
    Final: 'si_status_final',
    Discarded: 'si_status_discarded',
};

// Eenheden zoals op de factuur (doos, colli, stuk); onbekend blijft zoals het is.
const UNIT_KEYS: Record<string, string> = { box: 'si_unit_box', colli: 'si_unit_colli', piece: 'si_unit_piece', tray: 'si_unit_tray', dozen: 'si_unit_dozen' };
export function unitLabelKey(unit: string): string | null {
    return UNIT_KEYS[unit] ?? null;
}

// Wat Definitief nodig heeft, zoals de server het controleert: een concept met minstens één regel en een prijs op elke regel.
export function finalizeBlockers(invoice: Pick<SalesInvoice, 'status' | 'lines' | 'totals'>): string[] {
    if (invoice.status !== 'Concept') return ['si_not_concept'];
    const blockers: string[] = [];
    if (invoice.lines.length === 0) blockers.push('si_needs_lines');
    if (invoice.totals.linesWithoutPrice > 0 || invoice.lines.some((line) => line.unitPrice === null)) blockers.push('si_needs_prices');
    return blockers;
}

// Een concept uit de lijst kan mee in een selectie als geen enkele regel een prijs mist; een nultotaal (gratis of 100% korting) is geldig.
// De server controleert opnieuw, ook of er regels zijn.
export function selectable(item: SalesInvoiceListItem): boolean {
    return item.status === 'Concept' && item.linesWithoutPrice === 0;
}

// Het nummer zoals in de lijst: het factuurnummer, of "Concept" zolang er geen nummer is.
export function invoiceTitle(item: { invoiceNumber: string | null; status: SalesInvoiceStatus }, conceptLabel: string): string {
    return item.invoiceNumber ?? conceptLabel;
}
