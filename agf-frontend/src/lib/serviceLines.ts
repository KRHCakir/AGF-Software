// Dienstregels op een verkooporder (feedback 22 november): doorberekende handling of transport met een Dienst-artikel, aantal, prijs,
// korting en het btw-tarief van het artikel (standaard 21%). Geen voorraad, niet op laadbon of transportopdracht. Spiegel van
// SalesOrderService.Services; de server is leidend. Pure module zonder React (node:test); imports met .ts-extensie.

import type { CreateSalesOrderServiceLineRequest } from '../types/salesOrder';
import { parsePositiveDecimal, type DecimalError } from './decimal.ts';
import { type AmountLine, type DiscountError, isVatRate, lineAmount, parseDiscount } from './orderLineMath.ts';
import { parseUnitPrice } from './packaging.ts';

export const MAX_SERVICE_LINES = 50;
export const DEFAULT_SERVICE_VAT = 21;

export interface ServiceLineDraft {
    key: string;
    costArticleId: string;
    quantity: string;
    unitPrice: string;
    discount: string;
    // Leeg = het tarief van het artikel (voorstel); gekozen = dat tarief.
    vat: string;
}

export const emptyServiceLine = (key: string): ServiceLineDraft => ({ key, costArticleId: '', quantity: '1', unitPrice: '', discount: '', vat: '' });

// Het tarief van een regel: gekozen, anders dat van het artikel, anders 21%.
export function serviceVat(line: Pick<ServiceLineDraft, 'vat'>, articleVat: number | null | undefined): number {
    const chosen = Number(line.vat);
    if (line.vat !== '' && isVatRate(chosen)) return chosen;
    return articleVat != null && isVatRate(articleVat) ? articleVat : DEFAULT_SERVICE_VAT;
}

export interface ServiceLineRow {
    quantity: number | null;
    price: number | null;
    discount: number | null;
    vat: number;
    amount: number | null;
}

export function serviceLineRow(line: ServiceLineDraft, articleVat: number | null | undefined): ServiceLineRow {
    const quantity = parsePositiveDecimal(line.quantity);
    const price = parseUnitPrice(line.unitPrice);
    const discount = parseDiscount(line.discount);
    const q = quantity.ok ? quantity.value : null;
    const p = price.ok ? price.value : null;
    const d = discount.ok ? discount.value : null;
    return { quantity: q, price: p, discount: d, vat: serviceVat(line, articleVat), amount: lineAmount(q, p, d) };
}

export const serviceAmounts = (lines: readonly ServiceLineDraft[], vatOf: (costArticleId: string) => number | null | undefined): AmountLine[] =>
    lines.map((line) => { const row = serviceLineRow(line, vatOf(line.costArticleId)); return { amount: row.amount, vatPercentage: row.vat }; });

export type ServiceLineError = { field: string; code: DecimalError | DiscountError | 'required' | 'too_many_lines' };

// Alle dienstregels tegelijk, met de veldsleutels van de server (serviceLines[0].quantity). De btw gaat altijd mee (voorstel of keuze).
export function buildServiceLines(lines: readonly ServiceLineDraft[], vatOf: (costArticleId: string) => number | null | undefined):
    { ok: true; value: CreateSalesOrderServiceLineRequest[] } | { ok: false; errors: ServiceLineError[] } {
    const errors: ServiceLineError[] = [];
    if (lines.length > MAX_SERVICE_LINES) errors.push({ field: 'serviceLines', code: 'too_many_lines' });
    const value = lines.map((line, index) => {
        const prefix = `serviceLines[${index}]`;
        if (!/^[1-9]\d*$/.test(line.costArticleId)) errors.push({ field: `${prefix}.costArticleId`, code: 'required' });
        const quantity = parsePositiveDecimal(line.quantity);
        if (!quantity.ok) errors.push({ field: `${prefix}.quantity`, code: line.quantity.trim() === '' ? 'required' : quantity.error });
        const price = parseUnitPrice(line.unitPrice);
        if (!price.ok) errors.push({ field: `${prefix}.unitPrice`, code: price.error });
        const discount = parseDiscount(line.discount);
        if (!discount.ok) errors.push({ field: `${prefix}.discountPercentage`, code: discount.error });
        return {
            costArticleId: Number(line.costArticleId),
            quantity: quantity.ok ? quantity.value : 0,
            ...(price.ok && price.value !== null ? { unitPrice: price.value } : {}),
            ...(discount.ok && discount.value !== null ? { discountPercentage: discount.value } : {}),
            vatPercentage: serviceVat(line, vatOf(line.costArticleId)),
        };
    });
    return errors.length > 0 ? { ok: false, errors } : { ok: true, value };
}
