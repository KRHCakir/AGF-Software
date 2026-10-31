import type { Article } from '../types/article';
import type { ProductOption, VariantOption } from '../types/referenceData';
import { countryKey } from './countries.ts';
import type { EnteredAttributes, ReceiptAttribute } from './receiptBlindEntry';

// "Artikel aanmaken en doorgaan" vanuit aanvoer of ontvangst: wat er precies wordt aangemaakt, welke bestaande varianten er sterk op
// lijken ("Bedoel je dit?") en het aangemaakte artikel als keuzelijst-variant. Vergelijken gaat zoals de server (ArticleRules.AttributeKey):
// hoofdletters, accenten en dubbele spaties tellen niet, leeg is "geen" en een echte waarde.

export type SimilarAttribute = 'origin' | ReceiptAttribute;

export interface SimilarVariant {
    variant: VariantOption;
    // Het ene kenmerk dat verschilt; null = alleen de schrijfwijze verschilt (hoofdletters, spaties).
    differs: SimilarAttribute | null;
}

const key = (value: string | null | undefined) => countryKey(value ?? '');

const valueOf = (variant: VariantOption, attribute: ReceiptAttribute): string => {
    switch (attribute) {
        case 'size': return variant.sizeCategory ?? '';
        case 'packaging': return variant.packaging ?? '';
        case 'tradeClass': return variant.tradeClass ?? '';
        case 'brand': return variant.brand ?? '';
    }
};

const ATTRIBUTES: readonly ReceiptAttribute[] = ['size', 'packaging', 'tradeClass', 'brand'];

function sameOrigin(variant: VariantOption, origin: string | null, countryNames: ReadonlyMap<string, string>): boolean {
    if (origin === null) return !variant.originCountryCode && key(variant.origin) === '';
    if (variant.originCountryCode) return variant.originCountryCode === origin;
    return key(variant.origin) !== '' && key(variant.origin) === key(countryNames.get(origin) ?? origin);
}

// Bestaande varianten van hetzelfde artikel die sterk lijken op de invoer: alles gelijk (alleen andere schrijfwijze) of precies één
// kenmerk anders (herkomst, maat, verpakking, klasse of merk). Gearchiveerde varianten nooit. Alles gelijk staat bovenaan.
export function similarVariants(variants: readonly VariantOption[], entered: EnteredAttributes, countryNames: ReadonlyMap<string, string>): SimilarVariant[] {
    const result: SimilarVariant[] = [];
    for (const variant of variants) {
        if (variant.isArchived) continue;
        const different: SimilarAttribute[] = [];
        if (!sameOrigin(variant, entered.origin, countryNames)) different.push('origin');
        for (const attribute of ATTRIBUTES) if (key(valueOf(variant, attribute)) !== key(entered[attribute])) different.push(attribute);
        if (different.length <= 1) result.push({ variant, differs: different[0] ?? null });
    }
    return result.sort((a, b) => Number(a.differs !== null) - Number(b.differs !== null) || a.variant.id - b.variant.id);
}

// De kenmerken van een bestaande variant als invoer (bij "Kies dit").
export function enteredFrom(variant: VariantOption): EnteredAttributes {
    return {
        origin: variant.originCountryCode ?? null,
        size: variant.sizeCategory ?? '',
        packaging: variant.packaging ?? '',
        tradeClass: variant.tradeClass ?? '',
        brand: variant.brand ?? '',
    };
}

type Translate = (key: string, params?: Record<string, string>) => string;

// "Avocado Hass · Zuid-Afrika · maat 12 · verpakking test · klasse I · merk test"; een leeg kenmerk wordt "geen" (nooit lege tekst).
export function newArticleText(t: Translate, articleName: string, countryName: string | null, entered: EnteredAttributes): string {
    const part = (labelKey: string, value: string) => `${t(labelKey)} ${value.trim() === '' ? t('articles_value_none') : value.trim().replace(/\s+/g, ' ')}`;
    return [
        articleName,
        countryName ?? t('articles_value_none'),
        part('new_article_size', entered.size),
        part('new_article_packaging', entered.packaging),
        part('new_article_trade_class', entered.tradeClass),
        part('new_article_brand', entered.brand),
    ].join(' · ');
}

// Het aangemaakte (of bestaande) artikel als variant in de keuzelijsten van dit scherm, zonder de lijsten opnieuw te laden.
export function withArticle(products: readonly ProductOption[], article: Article): ProductOption[] {
    const variant: VariantOption = {
        id: article.id, name: article.variantName, origin: article.origin, originCountryCode: article.originCountryCode ?? null, tradeClass: article.tradeClass,
        brand: article.brand, weightMethod: article.weightMethod, packaging: article.packaging, sizeCategory: article.sizeCategory, shortcutCode: article.shortcutCode,
        defaultPackagesPerPallet: article.defaultPackagesPerPallet, packagingUnit: article.packagingUnit, defaultNetKgPerPackage: article.defaultNetKgPerPackage,
        defaultGrossKgPerPackage: article.defaultGrossKgPerPackage, isArchived: article.isArchived === true,
    };
    const known = products.some((product) => product.id === article.productId);
    const next = products.map((product) => (product.id === article.productId
        ? { ...product, variants: [...product.variants.filter((existing) => existing.id !== article.id), variant] }
        : product));
    return known ? next : [...next, { id: article.productId, name: article.productName, variants: [variant] }];
}
