import { parseLocaleDecimal, parsePositiveDecimal, type NumberLocale } from './decimal.ts';
import { lineAmount } from './orderLineMath.ts';
import { parseUnitPrice } from './packaging.ts';
import type { PurchaseArticle } from './purchaseOrderLines.ts';
import { isBlankPurchaseLine } from './purchaseOrderLines.ts';
import { salesLinePayload, type SalesLineDraft } from './salesLine.ts';

// Verkoopfactuur 1, de directe factuur (zonder order): productregels met dezelfde invoer als de verkooporderregel, met een verplichte
// partij, en vrije of dienstregels (omschrijving of dienst-artikel, aantal, prijs, btw, omzetsoort). De server krijgt alle regels in één
// lijst: eerst de productregels, dan de vrije regels; fouten komen terug als lines[i].veld in die volgorde.

export interface ServiceDraft { key: string; costArticleId: string; description: string; quantity: string; unitPrice: string; vat: string; revenueTypeId: string }

export const emptyService = (key: string, revenueTypeId = ''): ServiceDraft => ({ key, costArticleId: '', description: '', quantity: '1', unitPrice: '', vat: '21', revenueTypeId });

export const isBlankService = (line: ServiceDraft): boolean => line.costArticleId === '' && line.description.trim() === '' && line.unitPrice.trim() === '';

export interface DirectLine {
    lineKind: 'Product' | 'Service';
    productVariantId?: number | null;
    partijId?: number | null;
    costArticleId?: number | null;
    description?: string | null;
    quantity: number | null;
    unit?: string | null;
    unitPrice: number | null;
    discountPercentage: number | null;
    vatPercentage: number | null;
    revenueTypeId?: number | null;
}

// Fouten per veld met de sleutels van het formulier: lines[i] voor productregels, services[j] voor vrije regels.
export function buildDirectLines(products: SalesLineDraft[], services: ServiceDraft[], articleOf: (variantId: number) => PurchaseArticle | undefined, locale?: NumberLocale)
    : { ok: true; value: DirectLine[] } | { ok: false; errors: Record<string, string> } {
    const errors: Record<string, string> = {};
    const value: DirectLine[] = [];
    products.forEach((line, index) => {
        if (isBlankPurchaseLine(line)) return;
        const prefix = `lines[${index}]`;
        if (!/^[1-9]\d*$/.test(line.productVariantId)) { errors[`${prefix}.productVariantId`] = 'si_direct_article_required'; return; }
        const built = salesLinePayload(line, prefix, articleOf(Number(line.productVariantId)));
        for (const error of built.errors) errors[error.field] = `supply_error_${error.code}`;
        if (!line.partijId) errors[`${prefix}.partijId`] = 'si_direct_lot_required';
        value.push({
            lineKind: 'Product', productVariantId: built.payload.productVariantId, partijId: line.partijId ?? null, quantity: built.payload.quantity, unit: built.payload.unit,
            unitPrice: built.payload.unitPrice ?? null, discountPercentage: built.payload.discountPercentage ?? null, vatPercentage: built.payload.vatPercentage ?? null,
        });
    });
    services.forEach((line, index) => {
        if (isBlankService(line)) return;
        const prefix = `services[${index}]`;
        if (line.costArticleId === '' && line.description.trim() === '') errors[`${prefix}.description`] = 'si_direct_description_required';
        const quantity = parsePositiveDecimal(line.quantity, locale);
        if (!quantity.ok) errors[`${prefix}.quantity`] = 'si_edit_quantity_invalid';
        const price = parseUnitPrice(line.unitPrice, locale);
        if (!price.ok) errors[`${prefix}.unitPrice`] = 'si_edit_price_invalid';
        value.push({
            lineKind: 'Service', costArticleId: line.costArticleId === '' ? null : Number(line.costArticleId), description: line.description.trim() === '' ? null : line.description.trim(),
            quantity: quantity.ok ? quantity.value : null, unitPrice: price.ok ? price.value : null, discountPercentage: null, vatPercentage: Number(line.vat),
            revenueTypeId: line.revenueTypeId === '' ? null : Number(line.revenueTypeId),
        });
    });
    if (value.length === 0) errors.lines = 'si_needs_lines';
    return Object.keys(errors).length > 0 ? { ok: false, errors } : { ok: true, value };
}

// Bedragen van de vrije regels voor de btw-samenvatting onder de regels (zelfde rekenregels als de order).
export function serviceDraftAmounts(services: ServiceDraft[], locale?: NumberLocale): { amount: number | null; vatPercentage: number | null }[] {
    return services.filter((line) => !isBlankService(line)).map((line) => {
        const quantity = parseLocaleDecimal(line.quantity, locale, { maxDecimals: 3 });
        const price = parseUnitPrice(line.unitPrice, locale);
        return { amount: quantity.ok && price.ok ? lineAmount(quantity.value, price.value, null) : null, vatPercentage: Number(line.vat) };
    });
}

// Een serverfout op lines[i] (de gecombineerde lijst) terug naar het veld van het formulier.
export function formKeyOf(serverKey: string, productIndexes: number[], serviceIndexes: number[]): string {
    const match = /^lines\[(\d+)\](.*)$/.exec(serverKey);
    if (match === null) return serverKey;
    const index = Number(match[1]);
    return index < productIndexes.length ? `lines[${productIndexes[index]}]${match[2]}` : `services[${serviceIndexes[index - productIndexes.length] ?? 0}]${match[2]}`;
}
