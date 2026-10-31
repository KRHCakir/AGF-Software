import type { DeviationPreview, DeviationVariant } from '../types/receiptDeviation';
import { UNIT_LABEL_KEYS } from './units.ts';

// Afwijkende ontvangst in het scherm: welke kenmerken het venster toont, wat er rood is en hoe "Ander artikel ontvangen" zoekt.
// De server bepaalt wat afwijkt (kinds, zie ReceiptDeviationRules.cs); het scherm kleurt alleen wat de server noemt.

export const DEVIATION_CODES = {
    confirmationRequired: 'DEVIATION_CONFIRMATION_REQUIRED',
    blockedAssigned: 'DEVIATION_BLOCKED_ASSIGNED',
    otherArticle: 'DEVIATION_OTHER_ARTICLE',
} as const;

export const DEVIATION_REASONS = ['SupplierDeliveredDifferently', 'OtherLotLoaded', 'Other'] as const;
export type DeviationReason = (typeof DEVIATION_REASONS)[number];

export const DEVIATION_REASON_LABEL_KEYS: Readonly<Record<string, string>> = {
    SupplierDeliveredDifferently: 'deviation_reason_supplier',
    OtherLotLoaded: 'deviation_reason_other_lot',
    Other: 'deviation_reason_other',
};

export const DEVIATION_KIND_LABEL_KEYS: Readonly<Record<string, string>> = {
    Origin: 'deviation_attr_origin',
    Size: 'deviation_attr_size',
    Packaging: 'deviation_attr_packaging',
    TradeClass: 'deviation_attr_trade_class',
    Brand: 'deviation_attr_brand',
    Other: 'deviation_kind_other',
    Weight: 'deviation_attr_weight',
};

// Alleen een afwijking in het gewicht (zelfde artikel): dan gaat de vraag over kg per verpakking, niet over kenmerken.
export function isWeightOnly(kinds: readonly string[]): boolean {
    return kinds.length === 1 && kinds[0] === 'Weight';
}

export interface DeviationRow {
    labelKey: string;
    expected: string;
    received: string;
    differs: boolean;
}

const show = (value: string | null) => (value === null || value.trim() === '' ? '–' : value.trim());

// "Avocado Hass"
export function articleName(variant: DeviationVariant): string {
    return [variant.productName, variant.variantName].filter((part) => part !== null && part.trim() !== '').map((part) => part!.trim()).join(' ');
}

// "Avocado Hass · ZA · 12 · Doos (colli)": artikel met de onderscheidende kenmerken die een waarde hebben.
export function variantLabel(variant: DeviationVariant): string {
    const packaging = [variant.packaging, variant.packagingUnit === null ? null : `(${variant.packagingUnit})`].filter((part) => part !== null && part.trim() !== '').join(' ');
    return [articleName(variant), variant.origin, variant.size, packaging, variant.tradeClass, variant.brand]
        .filter((part): part is string => part !== null && part.trim() !== '')
        .map((part) => part.trim())
        .join(' · ');
}

// Kenmerken onder elkaar, verwacht en ontvangen; alleen wat de server als afwijkend noemt is rood.
export function deviationRows(expected: DeviationVariant, received: DeviationVariant, kinds: readonly string[]): DeviationRow[] {
    const packaging = (variant: DeviationVariant) => [variant.packaging, variant.packagingUnit].filter((part) => part !== null && part.trim() !== '').join(' · ');
    return [
        { labelKey: 'deviation_attr_article', expected: articleName(expected), received: articleName(received), differs: false },
        { labelKey: 'deviation_attr_origin', expected: show(expected.origin), received: show(received.origin), differs: kinds.includes('Origin') },
        { labelKey: 'deviation_attr_size', expected: show(expected.size), received: show(received.size), differs: kinds.includes('Size') },
        { labelKey: 'deviation_attr_packaging', expected: show(packaging(expected)), received: show(packaging(received)), differs: kinds.includes('Packaging') },
        { labelKey: 'deviation_attr_trade_class', expected: show(expected.tradeClass), received: show(received.tradeClass), differs: kinds.includes('TradeClass') },
        { labelKey: 'deviation_attr_brand', expected: show(expected.brand), received: show(received.brand), differs: kinds.includes('Brand') },
    ];
}

// Korte naam van de verwachte en ontvangen regel in de gevolgen ("Peru", "Zuid-Afrika"): het eerste afwijkende kenmerk, anders het artikel.
export function shortNames(preview: Pick<DeviationPreview, 'expected' | 'received' | 'kinds'>): { expected: string; received: string } {
    const pick = (variant: DeviationVariant) => {
        if (preview.kinds.includes('Origin') && variant.origin) return variant.origin;
        if (preview.kinds.includes('Size') && variant.size) return variant.size;
        if (preview.kinds.includes('Packaging') && variant.packaging) return variant.packaging;
        if (preview.kinds.includes('TradeClass') && variant.tradeClass) return variant.tradeClass;
        if (preview.kinds.includes('Brand') && variant.brand) return variant.brand;
        return variantLabel(variant);
    };
    return { expected: pick(preview.expected), received: pick(preview.received) };
}

// Eenheid in lopende tekst ("colli"); een onbekende code blijft zichtbaar zoals hij is.
export function unitText(t: (key: string) => string, unit: string | null): string {
    if (unit === null) return '';
    const key = (UNIT_LABEL_KEYS as Readonly<Record<string, string>>)[unit];
    return key === undefined ? unit : t(key).toLowerCase();
}

// 409 van de server met een afwijkingscode en het verschil (vangnet als het venster vooraf niet getoond is).
export function deviationConflict(body: unknown): { code: string; deviation: unknown } | null {
    if (typeof body !== 'object' || body === null) return null;
    const record = body as Record<string, unknown>;
    const code = record.code;
    return typeof code === 'string' && (Object.values(DEVIATION_CODES) as string[]).includes(code) ? { code, deviation: record.deviation } : null;
}
