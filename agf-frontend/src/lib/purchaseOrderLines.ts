// De regels van Inkooporder aanmaken (feedback 11 november): de gebruiker vult alleen in wat per regel anders is (product, aantal,
// prijs, en met "Bruto aanpassen" de bruto). Eenheid, netto en bruto per colli, colli per pallet, merk, land, maat en inhoud komen uit
// het artikel. Pure module zonder React (node:test); imports met .ts-extensie.

import type { PackageUnit } from '../types/availability';
import type { CreateSupplyCommitmentLineRequest } from '../types/supply';
import { parsePositiveDecimal, type DecimalError } from './decimal.ts';
import { resolveLineWeight, weightPayload, type LineWeight } from './lineWeight.ts';
import { parseUnitPrice } from './packaging.ts';
import { lineTotal, packagesPerPallet, pallets, sumLines, type PurchaseLineTotals } from './purchaseLine.ts';
import { type DiscountError, isVatRate, parseDiscount, summarize } from './orderLineMath.ts';
import { normalizeGgn } from './ggn.ts';
import { ggnProblem } from './productGgn.ts';

export interface PurchaseLineDraft {
    key: string;
    productVariantId: string;
    quantity: string;
    unitPrice: string;
    // Bruto aangepast op deze regel ("Bruto aanpassen"); zonder: aantal × bruto per colli van het artikel.
    grossKg?: string;
    grossKgEntered?: boolean;
    // Netto zelf ingevuld (alleen de verkooporder, bij een artikel zonder standaard netto); de inkoopregel laat dit leeg.
    netKg?: string;
    netKgEntered?: boolean;
    // Eenheid van een opgeslagen regel (bij bewerken): die blijft zoals ze is opgeslagen. Een nieuwe regel heeft hier niets en neemt de
    // eenheid van het artikel; op de regel kiezen kan niet meer (fixes 5b). Een ander artikel kiezen maakt dit veld leeg.
    unit: PackageUnit | '';
    lotNumber: string;
    // Certificaten deel 2: GGN van het product (zie productGgn.ts); ggnTouched = door de gebruiker gezet of opgeslagen.
    ggn: string;
    ggnTouched?: boolean;
    // Feedback 22 november: korting in procenten (leeg = geen) en het btw-tarief van de regel. Zolang vatTouched onwaar is, geldt het
    // tarief van het artikel (voorstel); een opgeslagen regel is altijd "aangeraakt", ook zonder tarief (leeg blijft dan leeg).
    discount?: string;
    vat?: string;
    vatTouched?: boolean;
}

// Wat de regel van het artikel nodig heeft (een VariantOption voldoet).
export interface PurchaseArticle {
    packagingUnit: PackageUnit | null;
    defaultNetKgPerPackage?: number | null;
    defaultGrossKgPerPackage?: number | null;
    defaultPackagesPerPallet: number | null;
    vatPercentage?: number | null;
}

export const emptyPurchaseLine = (key: string): PurchaseLineDraft => ({ key, productVariantId: '', quantity: '', unitPrice: '', unit: '', lotNumber: '', ggn: '' });

// Een lege regel (bijvoorbeeld de nieuwe regel na Tab op het laatste veld) telt niet mee bij opslaan, zolang er een gevulde regel is.
export function isBlankPurchaseLine(line: PurchaseLineDraft): boolean {
    return line.productVariantId === '' && line.quantity.trim() === '' && line.unitPrice.trim() === '' && !line.grossKgEntered && !line.netKgEntered && line.lotNumber.trim() === ''
        && (line.ggn ?? '').trim() === '' && (line.discount ?? '').trim() === '' && !line.vatTouched;
}

export type PurchaseLineError = { field: 'productVariantId' | 'expectedQuantity' | 'expectedUnit' | 'expectedGrossWeightKg' | 'expectedNetWeightKg' | 'unitPrice' | 'ggn' | 'discountPercentage' | 'vatPercentage';
    code: DecimalError | DiscountError | 'required' | 'gross_below_net' | 'ggn_13_digits' };

export interface PurchaseLineRow {
    quantity: number | null;
    unit: PackageUnit | '';
    weight: LineWeight | null;
    price: number | null;
    discount: number | null;
    // Het btw-tarief van de regel: het gekozen tarief, anders dat van het artikel; null = geen (een oudere regel zonder tarief).
    vat: number | null;
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
    // Eenheid: de opgeslagen eenheid van de regel, anders die van het artikel. Heeft het artikel er geen, dan is dat een fout op de regel
    // ("leg de eenheid vast bij het artikel"); kiezen op de regel kan niet.
    const unit = line.unit !== '' ? line.unit : article?.packagingUnit ?? '';
    if (unit === '') errors.push({ field: 'expectedUnit', code: 'required' });
    // Netto uit het artikel, tenzij op deze regel ingevuld (verkooporder, artikel zonder standaard netto); bruto uit het artikel, tenzij aangepast.
    const resolved = resolveLineWeight({ grossKg: line.grossKg, grossKgEntered: line.grossKgEntered, netKg: line.netKg, netKgEntered: line.netKgEntered }, quantity, article ?? undefined);
    if (!resolved.ok) for (const error of resolved.errors) errors.push({ field: error.field === 'net' ? 'expectedNetWeightKg' : 'expectedGrossWeightKg', code: error.code });
    const price = parseUnitPrice(line.unitPrice);
    if (!price.ok) errors.push({ field: 'unitPrice', code: price.error });
    if (ggnProblem(line.ggn ?? '') === 'ggn_13_digits') errors.push({ field: 'ggn', code: 'ggn_13_digits' });
    const discount = parseDiscount(line.discount ?? '');
    if (!discount.ok) errors.push({ field: 'discountPercentage', code: discount.error });
    const vat = lineVatOf(line, article);
    const perPallet = packagesPerPallet(null, article?.defaultPackagesPerPallet);
    return {
        quantity, unit, weight: resolved.ok ? resolved.value : null, price: price.ok ? price.value : null, discount: discount.ok ? discount.value : null, vat,
        total: lineTotal(quantity, price.ok ? price.value : null, discount.ok ? discount.value : null), perPallet, pallets: pallets(quantity, perPallet), errors,
    };
}

// Het btw-tarief van een regel: gekozen (of opgeslagen) als vatTouched, anders het voorstel uit het artikel.
export function lineVatOf(line: Pick<PurchaseLineDraft, 'vat' | 'vatTouched'>, article: { vatPercentage?: number | null } | undefined): number | null {
    if (line.vatTouched) {
        const chosen = Number(line.vat ?? '');
        return (line.vat ?? '') !== '' && isVatRate(chosen) ? chosen : null;
    }
    const proposal = article?.vatPercentage;
    return proposal != null && isVatRate(proposal) ? proposal : null;
}

// Totalen onder de regels; extra = bedragen van andere regels op dezelfde order (emballage, diensten) voor de btw-samenvatting.
export function purchaseLineTotals(rows: readonly PurchaseLineRow[], extra: readonly { amount: number | null; vatPercentage: number | null }[] = []): PurchaseLineTotals {
    return {
        ...sumLines(rows.map((row) => ({ quantity: row.quantity, netKg: row.weight?.netWeightKg ?? null, grossKg: row.weight?.grossWeightKg ?? null, total: row.total, pallets: row.pallets,
            grossEntered: row.weight?.weightSource === 'Entered' }))),
        amounts: summarize([...rows.map((row) => ({ amount: row.total, vatPercentage: row.vat })), ...extra]),
    };
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
            ggn: normalizeGgn(line.ggn ?? '') === '' ? null : normalizeGgn(line.ggn ?? ''),
            palletCount: null,
            packagesPerPallet: null,
            ...(row.weight !== null ? weightPayload(row.weight, 'expectedNetWeightKg', 'expectedGrossWeightKg') : {}),
            ...(row.price !== null ? { unitPrice: row.price } : {}),
            discountPercentage: row.discount,
            vatPercentage: row.vat,
        };
        return { index, payload };
    });
    return { lines: payloads, errors };
}
