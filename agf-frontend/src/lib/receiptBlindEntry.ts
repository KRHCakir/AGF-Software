import type { VariantOption, ProductOption } from '../types/referenceData';
import { countryKey } from './countries.ts';

// Blinde invoer bij ontvangst: de gebruiker vult zelf in wat er is aangekomen (herkomst, maat, verpakking en zo nodig klasse en merk);
// pas bij registreren zoekt het scherm de variant van hetzelfde artikel die daar precies bij hoort. Er wordt nooit iets "bijna
// gelijks" gekozen en nooit een artikel aangemaakt; de server vergelijkt daarna op variant-id en gewicht (afwijkingsvenster).

export type ReceiptAttribute = 'size' | 'packaging' | 'tradeClass' | 'brand';
export const RECEIPT_ATTRIBUTES: readonly ReceiptAttribute[] = ['size', 'packaging', 'tradeClass', 'brand'];

export interface EnteredAttributes {
    // Land (ISO-code) uit de landkiezer; null = nog niet gekozen.
    origin: string | null;
    size: string;
    packaging: string;
    tradeClass: string;
    brand: string;
}

export const emptyEntered = (): EnteredAttributes => ({ origin: null, size: '', packaging: '', tradeClass: '', brand: '' });

export type VariantMatch = { kind: 'match'; variantId: number } | { kind: 'none' } | { kind: 'ambiguous' };

const key = (value: string | null | undefined) => countryKey(value ?? '');

const attributeOf = (variant: VariantOption, attribute: ReceiptAttribute): string => {
    switch (attribute) {
        case 'size': return variant.sizeCategory ?? '';
        case 'packaging': return variant.packaging ?? '';
        case 'tradeClass': return variant.tradeClass ?? '';
        case 'brand': return variant.brand ?? '';
    }
};

// Varianten van hetzelfde artikel als de bestelde: zelfde product en zelfde ras/naam (zoals ReceiptDeviationRules.IsSameArticle).
export function articleVariants(products: readonly ProductOption[], orderedVariantId: number): VariantOption[] {
    for (const product of products) {
        const ordered = product.variants.find((variant) => variant.id === orderedVariantId);
        if (ordered) return product.variants.filter((variant) => key(variant.name) === key(ordered.name));
    }
    return [];
}

// Klasse en merk alleen tonen als de varianten van dit artikel daarin verschillen; herkomst, maat en verpakking altijd.
export function shownAttributes(variants: readonly VariantOption[]): ReceiptAttribute[] {
    return RECEIPT_ATTRIBUTES.filter((attribute) => attribute === 'size' || attribute === 'packaging'
        || new Set(variants.map((variant) => key(attributeOf(variant, attribute)))).size > 1);
}

// Een veld is verplicht als minstens één variant er een waarde voor heeft; leeg past dan nooit.
export function requiredFields(variants: readonly VariantOption[], shown: readonly ReceiptAttribute[]): { origin: boolean; attributes: ReceiptAttribute[] } {
    return {
        origin: variants.some((variant) => variant.originCountryCode || variant.origin),
        attributes: shown.filter((attribute) => variants.some((variant) => attributeOf(variant, attribute).trim() !== '')),
    };
}

function originMatches(variant: VariantOption, origin: string | null, countryNames: ReadonlyMap<string, string>): boolean {
    if (origin === null) return !variant.originCountryCode && !variant.origin;
    if (variant.originCountryCode) return variant.originCountryCode === origin;
    // Oude variant zonder land: dan de tekst tegen de naam van het gekozen land (zonder hoofdletters en accenten).
    return key(variant.origin) !== '' && key(variant.origin) === key(countryNames.get(origin) ?? origin);
}

function attributesMatch(variant: VariantOption, entered: EnteredAttributes, shown: readonly ReceiptAttribute[], skip: ReceiptAttribute | null): boolean {
    return shown.every((attribute) => attribute === skip || key(attributeOf(variant, attribute)) === key(entered[attribute]));
}

// Suggesties voor een veld: de waarden van de varianten die passen bij wat er al in de andere velden staat (lege velden tellen niet).
export function optionsFor(variants: readonly VariantOption[], entered: EnteredAttributes, shown: readonly ReceiptAttribute[], attribute: ReceiptAttribute,
    countryNames: ReadonlyMap<string, string>): string[] {
    const filled = shown.filter((other) => other !== attribute && entered[other].trim() !== '');
    const values = variants
        .filter((variant) => entered.origin === null || originMatches(variant, entered.origin, countryNames))
        .filter((variant) => filled.every((other) => key(attributeOf(variant, other)) === key(entered[other])))
        .map((variant) => attributeOf(variant, attribute).trim())
        .filter((value) => value !== '');
    return [...new Set(values)].sort((a, b) => a.localeCompare(b, 'nl', { numeric: true }));
}

// De variant die precies bij de invoer hoort. Passen er meer (gelijke kenmerken), dan de bestelde als die erbij zit; anders nooit raden.
export function matchVariant(variants: readonly VariantOption[], entered: EnteredAttributes, shown: readonly ReceiptAttribute[], orderedVariantId: number,
    countryNames: ReadonlyMap<string, string>): VariantMatch {
    const matches = variants.filter((variant) => originMatches(variant, entered.origin, countryNames) && attributesMatch(variant, entered, shown, null));
    if (matches.length === 0) return { kind: 'none' };
    if (matches.some((variant) => variant.id === orderedVariantId)) return { kind: 'match', variantId: orderedVariantId };
    return matches.length === 1 ? { kind: 'match', variantId: matches[0].id } : { kind: 'ambiguous' };
}
