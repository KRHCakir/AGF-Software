// De regels van Inkooporder aanmaken (feedback 11 november): de gebruiker vult alleen in wat per regel anders is (product, aantal,
// prijs, en met "Bruto aanpassen" de bruto). Eenheid, netto en bruto per colli, colli per pallet, merk, land, maat en inhoud komen uit
// het artikel. Pure module zonder React (node:test); imports met .ts-extensie.

import type { PackageUnit } from '../types/availability';
import type { CreateSupplyCommitmentLineRequest } from '../types/supply';
import { parsePositiveDecimal, type DecimalError } from './decimal.ts';
import { resolveLineWeight, weightPayload, type LineWeight } from './lineWeight.ts';
import { parseUnitPrice } from './packaging.ts';
import { lineTotal, packagesPerPallet, pallets, sumLines, type PurchaseLineTotals } from './purchaseLine.ts';

export interface PurchaseLineDraft {
    key: string;
    productVariantId: string;
    quantity: string;
    unitPrice: string;
    // Bruto aangepast op deze regel ("Bruto aanpassen"); zonder: aantal × bruto per colli van het artikel.
    grossKg?: string;
    grossKgEntered?: boolean;
    // Alleen als het artikel geen eenheid heeft (dan kiest de gebruiker die op de regel), en bij klantgebonden aanvoer de partij.
    unit: PackageUnit | '';
    lotNumber: string;
}

// Wat de regel van het artikel nodig heeft (een VariantOption voldoet).
export interface PurchaseArticle {
    packagingUnit: PackageUnit | null;
    defaultNetKgPerPackage?: number | null;
    defaultGrossKgPerPackage?: number | null;
    defaultPackagesPerPallet: number | null;
}

export const emptyPurchaseLine = (key: string): PurchaseLineDraft => ({ key, productVariantId: '', quantity: '', unitPrice: '', unit: '', lotNumber: '' });

// Een lege regel (bijvoorbeeld de nieuwe regel na Tab op het laatste veld) telt niet mee bij opslaan, zolang er een gevulde regel is.
export function isBlankPurchaseLine(line: PurchaseLineDraft): boolean {
    return line.productVariantId === '' && line.quantity.trim() === '' && line.unitPrice.trim() === '' && !line.grossKgEntered && line.lotNumber.trim() === '';
}

export type PurchaseLineError = { field: 'productVariantId' | 'expectedQuantity' | 'expectedUnit' | 'expectedGrossWeightKg' | 'unitPrice'; code: DecimalError | 'required' | 'gross_below_net' };

export interface PurchaseLineRow {
    quantity: number | null;
    unit: PackageUnit | '';
    // Eenheid vast uit het artikel (geen keuze op de regel).
    unitFromArticle: boolean;
    weight: LineWeight | null;
    price: number | null;
    total: number | null;
    perPallet: number | null;
    pallets: number | null;
    errors: PurchaseLineError[];
}

export function purchaseLineRow(line: PurchaseLineDraft, article: PurchaseArticle | undefined): PurchaseLineRow {
    const errors: PurchaseLineError[] = [];
    if (line.productVariantId === '') errors.push({ field: 'productVariantId', code: 'required' });
    const parsed = parsePositiveDecimal(line.quantity);
    const quantity = parsed.ok ? parsed.value : null;
    if (!parsed.ok) errors.push({ field: 'expectedQuantity', code: line.quantity.trim() === '' ? 'required' : parsed.error });
    const unitFromArticle = article?.packagingUnit != null;
    const unit = article?.packagingUnit ?? line.unit;
    if (unit === '') errors.push({ field: 'expectedUnit', code: 'required' });
    // Netto altijd uit het artikel (geen invoer meer); bruto uit het artikel, tenzij op deze regel aangepast.
    const resolved = resolveLineWeight({ grossKg: line.grossKg, grossKgEntered: line.grossKgEntered }, quantity, article ?? undefined);
    if (!resolved.ok) for (const error of resolved.errors) errors.push({ field: 'expectedGrossWeightKg', code: error.code });
    const price = parseUnitPrice(line.unitPrice);
    if (!price.ok) errors.push({ field: 'unitPrice', code: price.error });
    const perPallet = packagesPerPallet(null, article?.defaultPackagesPerPallet);
    return {
        quantity, unit, unitFromArticle, weight: resolved.ok ? resolved.value : null, price: price.ok ? price.value : null,
        total: lineTotal(quantity, price.ok ? price.value : null), perPallet, pallets: pallets(quantity, perPallet), errors,
    };
}

export function purchaseLineTotals(rows: readonly PurchaseLineRow[]): PurchaseLineTotals {
    return sumLines(rows.map((row) => ({ quantity: row.quantity, netKg: row.weight?.netWeightKg ?? null, grossKg: row.weight?.grossWeightKg ?? null, total: row.total, pallets: row.pallets })));
}

// De regels die worden opgeslagen (met hun plek in het formulier, voor de foutsleutels lines[i].veld) en hun payload.
export function purchaseLinePayloads(lines: readonly PurchaseLineDraft[], articleOf: (variantId: number) => PurchaseArticle | undefined): {
    lines: { index: number; payload: CreateSupplyCommitmentLineRequest }[];
    errors: { index: number; error: PurchaseLineError }[];
} {
    const filled = lines.map((line, index) => ({ line, index })).filter(({ line }) => !isBlankPurchaseLine(line));
    // Alleen lege regels: alleen de eerste krijgt de meldingen (product en aantal verplicht), niet elke lege regel dezelfde.
    const chosen = filled.length > 0 ? filled : lines.slice(0, 1).map((line, index) => ({ line, index }));
    const errors: { index: number; error: PurchaseLineError }[] = [];
    const payloads = chosen.map(({ line, index }) => {
        const row = purchaseLineRow(line, articleOf(Number(line.productVariantId)));
        for (const error of row.errors) errors.push({ index, error });
        const lot = line.lotNumber.trim();
        const payload: CreateSupplyCommitmentLineRequest = {
            productVariantId: Number(line.productVariantId),
            expectedQuantity: row.quantity ?? 0,
            expectedUnit: (row.unit === '' ? 'box' : row.unit) as PackageUnit,
            expectedNetWeightKg: null,
            lotNumber: lot === '' ? null : lot,
            palletCount: null,
            packagesPerPallet: null,
            ...(row.weight !== null ? weightPayload(row.weight, 'expectedNetWeightKg', 'expectedGrossWeightKg') : {}),
            ...(row.price !== null ? { unitPrice: row.price } : {}),
        };
        return { index, payload };
    });
    return { lines: payloads, errors };
}
