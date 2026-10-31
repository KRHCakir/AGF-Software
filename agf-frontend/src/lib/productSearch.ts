// Pure zoeklogica voor het doorzoekbare productveld, los van React zodat ze met node:test te toetsen is.
// Zoekt op product, variant, verpakking, herkomst, maatklasse en de bestaande snelcode (ShortcutCode) van een variant.
// Een snelcode kiest uitsluitend een productvariant; hoeveelheid of gewicht wordt hier nooit uit afgeleid.

export interface SearchableVariant {
    id: number;
    name: string;
    origin: string | null;
    // Land van herkomst (ISO-code): "PE" vindt ook de varianten uit Peru.
    originCountryCode?: string | null;
    packaging: string | null;
    sizeCategory: string | null;
    shortcutCode: string | null;
    // Gearchiveerd: nooit meer als suggestie; een bestaande regel met dit artikel vindt het wel terug (findVariant).
    isArchived?: boolean;
}

export interface SearchableProduct {
    id: number;
    name: string;
    variants: SearchableVariant[];
}

export interface VariantMatch {
    variantId: number;
    productName: string;
    variant: SearchableVariant;
    // De volledige omschrijving die na selectie zichtbaar blijft.
    description: string;
    // Alleen waar als de zoekterm precies de snelcode van deze variant is (hoofdletterongevoelig).
    exactShortcut: boolean;
}

export const MAX_SUGGESTIONS = 12;

function normalize(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

export function describeVariant(productName: string, variant: SearchableVariant): string {
    return [productName, variant.name, variant.packaging, variant.origin, variant.sizeCategory]
        .filter((part): part is string => typeof part === 'string' && part.trim() !== '')
        .join(' · ');
}

function haystack(productName: string, variant: SearchableVariant): string {
    return normalize([productName, variant.name, variant.packaging ?? '', variant.origin ?? '', variant.originCountryCode ?? '', variant.sizeCategory ?? '', variant.shortcutCode ?? ''].join(' '));
}

// Elke door spaties gescheiden term moet voorkomen (in willekeurige volgorde). Een exacte snelcode staat bovenaan.
export function searchVariants(products: readonly SearchableProduct[], query: string): VariantMatch[] {
    const term = normalize(query);
    if (term === '') return [];
    const words = term.split(/\s+/).filter((word) => word !== '');
    const matches: VariantMatch[] = [];
    for (const product of products) {
        for (const variant of product.variants) {
            if (variant.isArchived) continue;
            const exactShortcut = variant.shortcutCode !== null && normalize(variant.shortcutCode) === term;
            const text = haystack(product.name, variant);
            if (exactShortcut || words.every((word) => text.includes(word))) {
                matches.push({ variantId: variant.id, productName: product.name, variant, description: describeVariant(product.name, variant), exactShortcut });
            }
        }
    }
    matches.sort((a, b) => Number(b.exactShortcut) - Number(a.exactShortcut) || a.description.localeCompare(b.description));
    return matches;
}

// Een suggestie is "duidelijk" als Tab haar zonder navigatie mag accepteren: precies één treffer, of een exacte snelcode.
// Bij meerdere treffers zonder exacte snelcode is de invoer ambigu en wordt niets stilzwijgend gekozen.
export function unambiguousMatch(matches: readonly VariantMatch[]): VariantMatch | null {
    if (matches.length === 1) return matches[0];
    const exact = matches.filter((match) => match.exactShortcut);
    return exact.length === 1 ? exact[0] : null;
}

export function findVariant(products: readonly SearchableProduct[], variantId: number): VariantMatch | null {
    for (const product of products) {
        const variant = product.variants.find((candidate) => candidate.id === variantId);
        if (variant) return { variantId, productName: product.name, variant, description: describeVariant(product.name, variant), exactShortcut: false };
    }
    return null;
}
