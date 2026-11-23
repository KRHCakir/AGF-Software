// Herleidbaarheid als CSV (feedback 22 november: af te drukken en te exporteren voor een recall). Pure module, te toetsen met node:test.
// Puntkomma als scheiding en getallen zoals op het scherm (nl: komma), zodat Excel het in Nederland meteen goed opent.
import type { PartijRecall, SalesLotTrace } from '../types/partij';

export type Cell = string | number | null | undefined;

// Excel leest een cel die (na voorloopwitruimte) met = + - @ begint, of met tab/CR, als formule; een apostrof ervoor maakt er tekst van.
export function neutralizeFormula(text: string): string {
    return /^\s*[=+\-@]|^[\t\r]/.test(text) ? `'${text}` : text;
}

export function csvCell(value: Cell, number: (value: number) => string): string {
    // Getallen zijn onze eigen opmaak (ook negatief); tekst uit gegevensvelden kan als formule worden gelezen, dus neutraliseren we die.
    const text = value === null || value === undefined ? '' : typeof value === 'number' ? number(value) : neutralizeFormula(value);
    return /[;"\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

export function csvRows(rows: Cell[][], number: (value: number) => string): string {
    return rows.map((row) => row.map((cell) => csvCell(cell, number)).join(';')).join('\r\n') + '\r\n';
}

export interface RecallLabels {
    lot: string; received: string; shipped: string; returned: string; rejected: string; onHand: string; difference: string;
    salesLot: string; customer: string; reference: string; quantity: string; unit: string; date: string; directInvoice: string; directInvoiceNote: string;
}

// Inkoop-partij: eerst de som (ontvangen, geleverd, teruggenomen, afgekeurd, op voorraad, verschil), dan per bestemming een regel. Elke
// hoeveelheid staat naast haar eenheid (review): een kg-regel leest nooit als colli. Bij directe facturen de uitleg van het scherm erbij.
export function recallCsv(number: string, recall: PartijRecall, labels: RecallLabels, format: { number: (value: number) => string; date: (iso: string) => string }): string {
    const rows: Cell[][] = [
        [labels.lot, number],
        ...([[labels.received, recall.received], [labels.shipped, recall.shipped], [labels.returned, recall.returned],
            [labels.rejected, recall.rejected], [labels.onHand, recall.onHand], [labels.difference, recall.difference]] as Cell[][]).map((row) => [...row, recall.unit]),
        [],
        [labels.salesLot, labels.customer, labels.reference, labels.quantity, labels.unit, labels.date],
        ...recall.destinations.map((d) => [d.salesPartijNumber, d.customerName, d.reference, d.quantity, d.unit, d.date === null ? null : format.date(d.date)]),
        ...recall.directInvoices.map((d) => [d.salesPartijNumber, d.customerName, `${labels.directInvoice} ${d.reference ?? ''}`.trim(), d.quantity, d.unit, d.date === null ? null : format.date(d.date)]),
        ...(recall.directInvoices.length > 0 ? [[], [labels.directInvoiceNote]] : []),
    ];
    return csvRows(rows, format.number);
}

export interface DeliveryLabels { lot: string; customer: string; reference: string; date: string; product: string; quantity: string; unit: string; purchaseLot: string; supplier: string; origin: string; status: string; delivered: string; planned: string }

// Verkoop-partij: klant, order, leverdatum en leverstatus (gepland of geleverd), dan per regel en inkoop-partij een regel (product, aantal en eenheid, inkoop-partij, leverancier, herkomst).
export function deliveryCsv(number: string, trace: SalesLotTrace, labels: DeliveryLabels, format: { number: (value: number) => string; date: (iso: string) => string }): string {
    const rows: Cell[][] = [
        [labels.lot, number], [labels.customer, trace.customerName], [labels.reference, trace.reference], [labels.date, trace.deliveryDate === null ? null : format.date(trace.deliveryDate)],
        [labels.status, trace.shipped ? labels.delivered : labels.planned],
        [],
        [labels.product, labels.quantity, labels.unit, labels.purchaseLot, labels.supplier, labels.origin],
        ...trace.lines.flatMap((line) => (line.sources.length === 0
            ? [[line.product, line.quantity, line.unit, null, null, null]]
            : line.sources.map((source) => [line.product, source.quantity, line.unit, source.number, source.vendorName, source.origin]))),
    ];
    return csvRows(rows, format.number);
}
