// De verkooporderregel in de indeling van de inkooporder (feedback 22 november): artikel, aantal, prijs, korting en btw zoals de
// inkoopregel (purchaseOrderLines.ts: eenheid, kg, inhoud en pallets uit het artikel, bruto aan te passen), plus wat de verkoop extra heeft:
// de bron (uit voorraad of uit verwachte aanvoer). Hier de regel naar de payload van POST /api/sales-orders, met de veldnamen van de server.
// Pure module zonder React (node:test); imports met .ts-extensie.

import type { PackageUnit } from '../types/availability';
import type { CreateSalesOrderLineRequest } from '../types/salesOrder';
import type { SupplySource } from '../types/salesSupply';
import { weightPayload } from './lineWeight.ts';
import { type PurchaseArticle, type PurchaseLineDraft, type PurchaseLineError, emptyPurchaseLine, purchaseLineRow } from './purchaseOrderLines.ts';

export type SalesLineDraft = PurchaseLineDraft & {
    // Uit voorraad of uit verwachte aanvoer; leeg = de server kiest (uit voorraad als het past). sourceTouched: door de gebruiker gekozen.
    supplySource?: SupplySource | '';
    sourceTouched?: boolean;
};

export const emptySalesLine = (key: string): SalesLineDraft => emptyPurchaseLine(key);

// De veldnamen van de inkoopregel (formulier) naar die van de verkooporder (server): lines[i].quantity in plaats van expectedQuantity.
export const SALES_LINE_FIELD: Record<PurchaseLineError['field'], string> = {
    productVariantId: 'productVariantId', expectedQuantity: 'quantity', expectedUnit: 'unit', expectedGrossWeightKg: 'grossWeightKg', unitPrice: 'unitPrice',
    ggn: 'ggn', discountPercentage: 'discountPercentage', vatPercentage: 'vatPercentage',
};

export interface SalesLineResult {
    payload: CreateSalesOrderLineRequest;
    // De eenheid van de regel: de opgeslagen of meegegeven eenheid, anders die van het artikel ('' = geen; dat is een fout).
    unit: PackageUnit | '';
    errors: { field: string; code: PurchaseLineError['code'] }[];
}

// Eén regel naar de payload: zoals de inkoopregel rekent, met de sleutels van de server (lines[i].quantity, .unit, .grossWeightKg).
// Kg, prijs, korting, btw en bron gaan alleen mee als ze er zijn, zodat een regel zonder dezelfde opdracht blijft als vroeger.
export function salesLinePayload(line: SalesLineDraft, prefix: string, article: PurchaseArticle | undefined): SalesLineResult {
    const row = purchaseLineRow(line, article);
    return {
        unit: row.unit,
        errors: row.errors.map((error) => ({ field: `${prefix}.${SALES_LINE_FIELD[error.field]}`, code: error.code })),
        payload: {
            productVariantId: Number(line.productVariantId),
            quantity: row.quantity ?? 0,
            unit: (row.unit === '' ? 'box' : row.unit) as PackageUnit,
            palletCount: null,
            packagesPerPallet: null,
            ...(row.weight !== null ? weightPayload(row.weight, 'netWeightKg', 'grossWeightKg') : {}),
            ...(line.supplySource ? { supplySource: line.supplySource } : {}),
            ...(row.price !== null ? { unitPrice: row.price } : {}),
            ...(row.discount !== null ? { discountPercentage: row.discount } : {}),
            ...(row.vat !== null ? { vatPercentage: row.vat } : {}),
        },
    };
}
