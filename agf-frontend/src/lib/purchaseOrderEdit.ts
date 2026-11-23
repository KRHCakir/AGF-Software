// Inkooporder aanpassen (stap 4a): het formulier van Inkooporder aanmaken gevuld met de opgeslagen order, wat per regel vastligt en de
// payload van PUT /api/purchase-orders/{id}. Spiegel van PurchaseOrderService.Edit.cs en PurchaseOrderDocumentRules.cs.
// Pure module zonder React (node:test); imports met .ts-extensie.

import type { CreateSupplyCommitmentLineRequest } from '../types/supply';
import type { PurchaseOrderDetail, PurchaseOrderLine, UpdatePurchaseOrderLineRequest } from '../types/purchaseOrder';
import { amsterdamDateOf } from './calendarDate.ts';
import { formatAmountInput, formatDecimalInput, formatKgInput } from './decimal.ts';
import type { PurchaseLineDraft } from './purchaseOrderLines.ts';
import type { PackageUnit } from '../types/availability';

export const DOCUMENT_OPEN = 'Open';
export const DOCUMENT_DEFINITIVE = 'Definitive';

// In de lijst: Aanpassen zolang de order Open is, voor het eigen magazijn en niet geannuleerd (zoals editable op het detail).
export const listEditable = (item: { documentStatus?: string | null; route: string; status: string }): boolean =>
    !isDefinitive(item) && item.route === 'Warehouse' && item.status !== 'Cancelled';

// Een oudere server zonder documentstatus: Open.
export const documentStatusOf = (order: { documentStatus?: string | null }): string => order.documentStatus ?? DOCUMENT_OPEN;
export const isDefinitive = (order: { documentStatus?: string | null }): boolean => documentStatusOf(order) === DOCUMENT_DEFINITIVE;

// De kop zoals het formulier hem kent (zelfde velden als bij aanmaken, plus de notitie).
export interface EditHeader {
    vendorId: string;
    // Land van de herkomst; legacyOrigin = een oude tekst zonder land (alleen getoond, blijft staan zolang er geen land gekozen wordt).
    originCountryCode: string | null;
    legacyOrigin: string | null;
    expectedDate: string;
    supplierReference: string;
    orderedOn: string;
    supplierLotReference: string;
    note: string;
}

// Per regel van de opgeslagen order: haar id, wat eraan hangt, wat er ontvangen is en hoe ze opende (om "niet aangeraakt" te herkennen).
export interface EditLine {
    lineId: number;
    inUse: boolean;
    received: number;
    stored: PurchaseOrderLine;
    opened: PurchaseLineDraft;
}

export interface OrderEdit {
    orderId: number;
    version: number;
    vendorFixed: boolean;
    partijFixed: boolean;
    partijId: number | null;
    partijNumber: string | null;
    // Op de sleutel van de regel in het formulier; een nieuwe regel staat er niet in.
    lines: ReadonlyMap<string, EditLine>;
}

const decimal = (value: number | null | undefined) => (value === null || value === undefined ? '' : formatDecimalInput(value));

export function lineDraftOf(line: PurchaseOrderLine): PurchaseLineDraft {
    const grossEntered = line.weightSource === 'Entered' && line.expectedGrossWeightKg !== null && line.expectedGrossWeightKg !== undefined;
    return {
        key: `line-${line.id}`,
        productVariantId: String(line.productVariantId),
        quantity: decimal(line.expectedQuantity),
        unitPrice: line.unitPrice === null || line.unitPrice === undefined ? '' : formatAmountInput(line.unitPrice),
        ...(grossEntered ? { grossKg: formatKgInput(line.expectedGrossWeightKg!), grossKgEntered: true } : {}),
        unit: (line.unit ?? '') as PackageUnit | '',
        lotNumber: line.lotNumber ?? '',
        // Een opgeslagen GGN (ook leeg) is een keuze: het voorstel overschrijft het niet.
        ggn: line.ggn ?? '',
        ggnTouched: true,
        // Feedback 22 november: korting en btw zoals opgeslagen; een regel zonder tarief houdt geen tarief (geen voorstel uit het artikel).
        discount: line.discountPercentage === null || line.discountPercentage === undefined ? '' : formatDecimalInput(line.discountPercentage, 2),
        vat: line.vatPercentage === null || line.vatPercentage === undefined ? '' : String(line.vatPercentage),
        vatTouched: true,
    };
}

export function orderEditOf(order: PurchaseOrderDetail): { header: EditHeader; lines: PurchaseLineDraft[]; edit: OrderEdit } {
    const drafts = order.lines.map(lineDraftOf);
    return {
        header: {
            vendorId: String(order.vendorId),
            originCountryCode: order.originCountryCode ?? null,
            legacyOrigin: (order.originCountryCode ?? null) === null && (order.origin ?? '').trim() !== '' ? order.origin.trim() : null,
            expectedDate: order.expectedAt === null ? '' : amsterdamDateOf(order.expectedAt) ?? '',
            supplierReference: order.supplierReference ?? '',
            orderedOn: order.orderedOn,
            supplierLotReference: order.supplierLotReference ?? '',
            note: order.note ?? '',
        },
        lines: drafts,
        edit: {
            orderId: order.id,
            version: order.editVersion ?? 0,
            vendorFixed: order.vendorFixed ?? false,
            partijFixed: order.partijFixed ?? false,
            partijId: order.partijId ?? null,
            partijNumber: order.partijNumber ?? null,
            lines: new Map(order.lines.map((line, index) => [drafts[index].key, {
                lineId: line.id, inUse: line.inUse ?? false, received: line.receivedQuantity, stored: line, opened: drafts[index],
            }])),
        },
    };
}

// Wat de gebruiker op de regel aan artikel, aantal, eenheid of bruto niet heeft veranderd: dan gaan de opgeslagen kg, palletinvoer en
// eenheid ongewijzigd terug (het formulier rekent netto anders opnieuw uit het artikel, ook als de artikelstandaard intussen veranderde).
export function isUntouched(draft: PurchaseLineDraft, edit: EditLine): boolean {
    const opened = edit.opened;
    return draft.productVariantId === opened.productVariantId && draft.quantity.trim() === opened.quantity && draft.unit === opened.unit
        && Boolean(draft.grossKgEntered) === Boolean(opened.grossKgEntered) && (draft.grossKgEntered ? (draft.grossKg ?? '').trim() === (opened.grossKg ?? '') : true);
}

// Feedback 22 november, splitsen: een deels ontvangen regel met een ander artikel wordt bij opslaan gesplitst (het ontvangen deel blijft op
// de oude regel, het open deel wordt een nieuwe regel met dit artikel). Wat er met het restant gebeurt, kiest de gebruiker.
export type SplitRemainder = 'Expected' | 'Cancelled';

export function isSplit(draft: PurchaseLineDraft, edit: EditLine | undefined): boolean {
    return edit !== undefined && edit.received > 0 && draft.productVariantId !== '' && draft.productVariantId !== edit.opened.productVariantId;
}

// Het open deel van de oude regel: besteld min ontvangen (het voorstel voor het aantal van de nieuwe regel).
export function openQuantityOf(edit: EditLine): number {
    return Math.max(0, edit.stored.expectedQuantity - edit.received);
}

// Het GGN van een opgeslagen regel gaat altijd mee: leeg als '' (bewust leegmaken), want weggelaten (null) betekent voor de server
// "ongewijzigd" (zo wist een oudere client nooit een GGN).
export function updateLinePayload(draft: PurchaseLineDraft, payload: CreateSupplyCommitmentLineRequest, edit: EditLine | undefined,
    remainder: SplitRemainder | null = null): UpdatePurchaseOrderLineRequest {
    const price = payload.unitPrice ?? null;
    if (edit === undefined) return { ...payload, id: null, unitPrice: price };
    if (isSplit(draft, edit)) return { ...payload, id: edit.lineId, unitPrice: price, ggn: payload.ggn ?? '', splitRemainder: remainder };
    if (!isUntouched(draft, edit)) return { ...payload, id: edit.lineId, unitPrice: price, ggn: payload.ggn ?? '' };
    const stored = edit.stored;
    return {
        id: stored.id, productVariantId: stored.productVariantId, expectedQuantity: stored.expectedQuantity, expectedUnit: stored.unit,
        expectedNetWeightKg: stored.expectedNetWeightKg, expectedGrossWeightKg: stored.expectedGrossWeightKg ?? null, weightSource: stored.weightSource ?? null,
        lotNumber: payload.lotNumber, ggn: payload.ggn ?? '', palletCount: stored.palletInputCount ?? null, packagesPerPallet: stored.palletInputPackagesPerPallet ?? null,
        loosePackages: stored.palletInputLoose ?? null, unitPrice: price,
        discountPercentage: payload.discountPercentage ?? null, vatPercentage: payload.vatPercentage ?? null,
    };
}

// De grenzen van een regel na ontvangst, met de uitleg die het formulier toont (nooit een grijs veld zonder reden).
// Een deels ontvangen regel mag een ander artikel krijgen (dan splitst ze, splitReceived = wat ontvangen is en op de oude regel blijft);
// een regel waaraan iets anders hangt (reservering, afwijking) houdt haar artikel.
export interface LineLimits {
    articleFixed: boolean;
    minimumQuantity: number | null;
    removable: boolean;
    splitReceived: number | null;
}

export function lineLimits(edit: EditLine | undefined, draft?: PurchaseLineDraft): LineLimits {
    if (edit === undefined || !edit.inUse) return { articleFixed: false, minimumQuantity: null, removable: true, splitReceived: null };
    if (edit.received <= 0) return { articleFixed: true, minimumQuantity: null, removable: false, splitReceived: null };
    const split = draft !== undefined && isSplit(draft, edit);
    return { articleFixed: false, minimumQuantity: split ? null : edit.received, removable: false, splitReceived: split ? edit.received : null };
}

// Opgeslagen regels die niet meer in het formulier staan, maar wel moeten blijven (er hangt iets aan): de server weigert dat ook.
export function removedInUse(edit: OrderEdit, keys: ReadonlySet<string>): EditLine[] {
    return [...edit.lines.entries()].filter(([key, line]) => line.inUse && !keys.has(key)).map(([, line]) => line);
}

// Leesbare naam van een veld uit de auditregel (sleutel in nl.json/en.json).
const FIELD_KEYS: Readonly<Record<string, string>> = {
    vendor: 'po_change_vendor', supplierReference: 'po_supplier_reference', orderedOn: 'po_ordered_on', expectedAt: 'supply_expected_date', origin: 'supply_origin',
    note: 'po_note', supplierLotReference: 'partij_supplier_lot', orderChannel: 'po_change_channel', partij: 'partij_field', line: 'po_change_line',
    article: 'po_change_article', expectedQuantity: 'po_change_quantity', expectedUnit: 'po_change_unit', unitPrice: 'po_change_price', discountPercentage: 'po_change_discount', vatPercentage: 'po_change_vat',
    expectedNetWeightKg: 'po_change_net', expectedGrossWeightKg: 'po_change_gross', weightSource: 'po_change_weight_source', pallets: 'po_change_pallets',
    lotNumber: 'supply_lot', ggn: 'po_line_col_ggn', split: 'po_change_split', cancelledQuantity: 'po_change_cancelled',
    receiveLocation: 'po_logistics_location', loadingAddress: 'po_logistics_loading', deliveryAddress: 'po_logistics_delivery', documentStatus: 'po_document_status', currency: 'po_change_currency', exchangeRate: 'po_change_exchange_rate',
};
export const changeFieldKey = (field: string): string | null => FIELD_KEYS[field] ?? null;
