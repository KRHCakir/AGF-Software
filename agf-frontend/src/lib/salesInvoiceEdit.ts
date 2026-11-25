import { formatDecimalInput, parseLocaleDecimal, parsePositiveDecimal, type NumberLocale } from './decimal.ts';
import { parseDiscount } from './orderLineMath.ts';
import { parseUnitPrice } from './packaging.ts';
import type { SalesInvoiceLine } from '../types/salesInvoice';

// Verkoopfactuur 1: een concept bewerken. Per regel aantal, prijs, korting en btw als tekst in de schermtaal (komma), of weghalen.
// Fouten met dezelfde sleutels als de server (lines[i].quantity), zodat beide op dezelfde plek staan.
export const VAT_CHOICES = ['9', '21', '0'] as const;

export interface LineDraft { id: number; quantity: string; unitPrice: string; discount: string; vat: string; remove: boolean; maxQuantity: number | null; packaging: boolean }

export function draftOf(line: SalesInvoiceLine, locale?: NumberLocale): LineDraft {
    return {
        id: line.id,
        quantity: formatDecimalInput(line.quantity, 3, locale),
        unitPrice: line.unitPrice === null ? '' : formatDecimalInput(line.unitPrice, 2, locale, 2),
        discount: line.discountPercentage === 0 ? '' : formatDecimalInput(line.discountPercentage, 2, locale),
        vat: String(line.vatPercentage),
        remove: false,
        maxQuantity: line.maxQuantity,
        packaging: line.lineKind === 'Packaging',
    };
}

export interface LineUpdate { id: number; quantity?: number; unitPrice?: number | null; discountPercentage?: number | null; vatPercentage?: number; remove?: boolean }

export function buildLineUpdates(drafts: LineDraft[], locale?: NumberLocale): { ok: true; value: LineUpdate[] } | { ok: false; errors: Record<string, string> } {
    const errors: Record<string, string> = {};
    const value = drafts.map((draft, index): LineUpdate => {
        if (draft.remove) return { id: draft.id, remove: true };
        const key = `lines[${index}]`;
        const quantity = draft.packaging ? parseLocaleDecimal(draft.quantity, locale, { maxDecimals: 0 }) : parsePositiveDecimal(draft.quantity, locale);
        if (!quantity.ok) errors[`${key}.quantity`] = 'si_edit_quantity_invalid';
        else if (draft.maxQuantity !== null && quantity.value > draft.maxQuantity) errors[`${key}.quantity`] = 'si_edit_quantity_too_much';
        const price = parseUnitPrice(draft.unitPrice, locale);
        if (!price.ok) errors[`${key}.unitPrice`] = 'si_edit_price_invalid';
        const discount = parseDiscount(draft.discount, locale);
        if (!discount.ok) errors[`${key}.discountPercentage`] = 'si_edit_discount_invalid';
        if (!(VAT_CHOICES as readonly string[]).includes(draft.vat)) errors[`${key}.vatPercentage`] = 'si_edit_vat_invalid';
        return {
            id: draft.id,
            quantity: quantity.ok ? quantity.value : undefined,
            unitPrice: price.ok ? price.value : undefined,
            discountPercentage: discount.ok ? discount.value : undefined,
            vatPercentage: Number(draft.vat),
        };
    });
    return Object.keys(errors).length > 0 ? { ok: false, errors } : { ok: true, value };
}
