import { PACKAGE_UNITS, PackageUnit } from './availability';
import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';
import { WeightMethod, isWeightMethod } from '../lib/weight';

// Contract van api/articles (backend: AgfAdmin/DTOs/ArticleDtos.cs). Eén artikel = één productvariant onder een productsoort.

export const GOODS_CODE_SYSTEMS = ['HS', 'CN', 'TARIC'] as const;
export type GoodsCodeSystem = (typeof GOODS_CODE_SYSTEMS)[number];

export function isGoodsCodeSystem(value: unknown): value is GoodsCodeSystem {
    return typeof value === 'string' && (GOODS_CODE_SYSTEMS as readonly string[]).includes(value);
}

export interface Article {
    id: number;
    productId: number;
    productName: string;
    variantName: string;
    description: string;
    shortcutCode: string | null;
    // Weergavenaam van de herkomst (Nederlandse landnaam) en het land zelf; zonder land = nog opschonen (oude tekst).
    origin: string | null;
    originCountryCode?: string | null;
    brand: string | null;
    sizeCategory: string | null;
    packaging: string | null;
    packagingUnit: PackageUnit | null;
    season: string | null;
    notes: string | null;
    weightMethod: WeightMethod;
    defaultNetKgPerPackage: number | null;
    defaultGrossKgPerPackage: number | null;
    defaultPackagesPerPallet: number | null;
    goodsCode: string | null;
    goodsCodeSystem: GoodsCodeSystem | null;
    tradeClass: string | null;
    defaultPrice: number;
    // Gearchiveerd (inactief): niet meer in keuzelijsten en suggesties; terugzetten kan altijd. Optioneel voor oudere antwoorden.
    isArchived?: boolean;
    archivedAt?: string | null;
    archivedBy?: string | null;
}

// GET api/articles/{id}/usage: waar het artikel aan hangt; verwijderen kan alleen zonder gebruik (anders archiveren).
export const ARTICLE_USAGE_KINDS = ['purchaseOrderLines', 'receipts', 'receiptDeviations', 'salesOrderLines', 'purchaseInvoiceLines', 'invoiceLines',
    'legacyOrderLines', 'inventory', 'wasteLogs'] as const;
export type ArticleUsageKind = (typeof ARTICLE_USAGE_KINDS)[number];

export interface ArticleUsage {
    articleId: number;
    canDelete: boolean;
    usages: { kind: string; count: number }[];
    message: string | null;
}

export function isArticleUsage(value: unknown): value is ArticleUsage {
    return isObject(value) && isNumber(value.articleId) && typeof value.canDelete === 'boolean' && isNullableString(value.message)
        && isArrayOf(value.usages, (usage): usage is { kind: string; count: number } => isObject(usage) && isString(usage.kind) && isNumber(usage.count));
}

// Productsoort zonder artikelen (mag worden verwijderd).
export interface EmptyProduct { id: number; name: string }

export function isEmptyProductList(value: unknown): value is EmptyProduct[] {
    return isArrayOf(value, (item): item is EmptyProduct => isObject(item) && isNumber(item.id) && isString(item.name));
}

export interface SaveArticleRequest {
    productName: string;
    variantName: string | null;
    shortcutCode: string | null;
    // Land uit de vaste lijst (ISO-code); verplicht. De server zet de landnaam als weergave.
    originCountryCode: string;
    brand: string | null;
    sizeCategory: string | null;
    packaging: string | null;
    packagingUnit: PackageUnit | null;
    season: string | null;
    notes: string | null;
    weightMethod: WeightMethod;
    defaultNetKgPerPackage: number | null;
    defaultGrossKgPerPackage: number | null;
    defaultPackagesPerPallet: number | null;
    goodsCode: string | null;
    goodsCodeSystem: GoodsCodeSystem | null;
    tradeClass: string | null;
}

export interface ArticleReference {
    productNames: string[];
    packagingUnits: string[];
    weightMethods: string[];
    goodsCodeSystems: string[];
    tradeClassSuggestions: string[];
}

export interface GoodsCodeSource {
    system: string;
    name: string;
    url: string | null;
    version: string | null;
    validFrom: string | null;
    retrievedAt: string | null;
    note: string | null;
    lookupUrl: string | null;
}

export interface GoodsCodeMatch {
    code: string;
    system: string;
    hsCode: string | null;
    descriptionNl: string | null;
    descriptionEn: string | null;
    // Alleen bij HS: de GN-codes uit de lijst die tot deze post zijn samengevoegd.
    mergedCodes: string[] | null;
}

export interface GoodsCodeSearch {
    // Het gezochte stelsel; matches en totalEntries gelden alleen voor dit stelsel.
    system: string;
    source: GoodsCodeSource;
    complete: boolean;
    totalEntries: number;
    matches: GoodsCodeMatch[];
}

const isNullableUnit = (value: unknown): value is PackageUnit | null => value === null || (isString(value) && (PACKAGE_UNITS as readonly string[]).includes(value));

export function isArticle(value: unknown): value is Article {
    return isObject(value) && isNumber(value.id) && isNumber(value.productId) && isString(value.productName) && isString(value.variantName)
        && isString(value.description) && isNullableString(value.shortcutCode) && isNullableString(value.origin) && isNullableString(value.brand)
        && isNullableString(value.sizeCategory) && isNullableString(value.packaging) && isNullableUnit(value.packagingUnit)
        && isNullableString(value.season) && isNullableString(value.notes) && isWeightMethod(value.weightMethod)
        && isNullableNumber(value.defaultNetKgPerPackage) && isNullableNumber(value.defaultGrossKgPerPackage) && isNullableNumber(value.defaultPackagesPerPallet)
        && isNullableString(value.goodsCode) && (value.goodsCodeSystem === null || isGoodsCodeSystem(value.goodsCodeSystem))
        && isNullableString(value.tradeClass) && isNumber(value.defaultPrice);
}

export function isArticleList(value: unknown): value is Article[] {
    return isArrayOf(value, isArticle);
}

export function isArticleReference(value: unknown): value is ArticleReference {
    return isObject(value) && isArrayOf(value.productNames, isString) && isArrayOf(value.packagingUnits, isString) && isArrayOf(value.weightMethods, isString)
        && isArrayOf(value.goodsCodeSystems, isString) && isArrayOf(value.tradeClassSuggestions, isString);
}

function isGoodsCodeSource(value: unknown): value is GoodsCodeSource {
    return isObject(value) && isString(value.system) && isString(value.name) && isNullableString(value.url) && isNullableString(value.version)
        && isNullableString(value.validFrom) && isNullableString(value.retrievedAt) && isNullableString(value.note) && isNullableString(value.lookupUrl);
}

function isGoodsCodeMatch(value: unknown): value is GoodsCodeMatch {
    return isObject(value) && isString(value.code) && isString(value.system) && isNullableString(value.hsCode) && isNullableString(value.descriptionNl) && isNullableString(value.descriptionEn)
        && (value.mergedCodes === null || isArrayOf(value.mergedCodes, isString));
}

export function isGoodsCodeSearch(value: unknown): value is GoodsCodeSearch {
    return isObject(value) && isString(value.system) && isGoodsCodeSource(value.source) && typeof value.complete === 'boolean' && isNumber(value.totalEntries) && isArrayOf(value.matches, isGoodsCodeMatch);
}
