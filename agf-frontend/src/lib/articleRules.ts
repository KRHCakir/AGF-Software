import type { PackageUnit } from '../types/availability';
import { PACKAGE_UNITS } from '../types/availability';
import type { Article, GoodsCodeSystem, SaveArticleRequest } from '../types/article';
import { GOODS_CODE_SYSTEMS, isGoodsCodeSystem } from '../types/article';
import { parsePositiveDecimal } from './decimal.ts';
import type { WeightMethod } from './weight.ts';
import { isWeightMethod } from './weight.ts';

// Pure regels van de artikelwerkplek, los van React en i18n (node:test). Spiegelt ArticleService/ArticleRules:
// alleen de productsoort is verplicht; leeg = niet vastgelegd; goederencodes zijn tekst (voorloopnullen blijven) met een
// expliciet codestelsel; bruto kg is informatief en nooit een vervanging van netto kg.

export interface ArticleDraft {
    productName: string;
    variantName: string;
    shortcutCode: string;
    origin: string;
    brand: string;
    sizeCategory: string;
    packaging: string;
    packagingUnit: PackageUnit | '';
    season: string;
    notes: string;
    weightMethod: WeightMethod;
    defaultNetKgPerPackage: string;
    defaultGrossKgPerPackage: string;
    defaultPackagesPerPallet: string;
    goodsCode: string;
    goodsCodeSystem: GoodsCodeSystem | '';
    tradeClass: string;
}

export type ArticleRuleCode = 'required' | 'empty' | 'invalid' | 'not_positive' | 'too_many_decimals' | 'too_large' | 'too_long' | 'gross_below_net' | 'system_required' | 'wrong_length';
export interface ArticleRuleError { field: string; code: ArticleRuleCode }
export type ArticleBuildResult = { ok: true; value: SaveArticleRequest } | { ok: false; errors: ArticleRuleError[] };

export const MAX_NAME_LENGTH = 100;
export const MAX_SHORTCUT_LENGTH = 32;
export const MAX_NOTES_LENGTH = 500;
export const MAX_TRADE_CLASS_LENGTH = 50;
const SHORTCUT = /^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$/;

export const GOODS_CODE_LENGTH: Readonly<Record<GoodsCodeSystem, number>> = { HS: 6, CN: 8, TARIC: 10 };

export function emptyArticleDraft(): ArticleDraft {
    return {
        productName: '', variantName: '', shortcutCode: '', origin: '', brand: '', sizeCategory: '', packaging: '', packagingUnit: '', season: '', notes: '',
        weightMethod: 'Weighed', defaultNetKgPerPackage: '', defaultGrossKgPerPackage: '', defaultPackagesPerPallet: '', goodsCode: '', goodsCodeSystem: '', tradeClass: '',
    };
}

const num = (value: number | null) => (value === null ? '' : String(value));

export function draftFromArticle(article: Article): ArticleDraft {
    return {
        productName: article.productName, variantName: article.variantName, shortcutCode: article.shortcutCode ?? '', origin: article.origin ?? '',
        brand: article.brand ?? '', sizeCategory: article.sizeCategory ?? '', packaging: article.packaging ?? '', packagingUnit: article.packagingUnit ?? '',
        season: article.season ?? '', notes: article.notes ?? '', weightMethod: article.weightMethod,
        defaultNetKgPerPackage: num(article.defaultNetKgPerPackage), defaultGrossKgPerPackage: num(article.defaultGrossKgPerPackage), defaultPackagesPerPallet: num(article.defaultPackagesPerPallet),
        goodsCode: article.goodsCode ?? '', goodsCodeSystem: article.goodsCodeSystem ?? '', tradeClass: article.tradeClass ?? '',
    };
}

// Alleen cijfers; spaties en punten uit de gebruikelijke schrijfwijze ("0804 40 00") vallen weg. Voorloopnullen blijven.
export function goodsCodeDigits(raw: string): string {
    return raw.replace(/[^0-9]/g, '');
}

// Leesbare schrijfwijze: 0804 40 00 (CN), 0804 40 (HS), 0804 40 00 90 (TARIC).
export function formatGoodsCode(code: string): string {
    const digits = goodsCodeDigits(code);
    return digits.replace(/^(\d{4})(\d{2})?(\d{2})?(\d{2})?$/, (_m, a: string, b?: string, c?: string, d?: string) => [a, b, c, d].filter((part) => part !== undefined).join(' '));
}

function text(value: string, max: number, field: string, errors: ArticleRuleError[]): string | null {
    const trimmed = value.trim();
    if (trimmed === '') return null;
    if (trimmed.length > max) errors.push({ field, code: 'too_long' });
    return trimmed;
}

function positive(value: string, field: string, errors: ArticleRuleError[]): number | null {
    if (value.trim() === '') return null;
    const parsed = parsePositiveDecimal(value);
    if (!parsed.ok) { errors.push({ field, code: parsed.error }); return null; }
    return parsed.value;
}

export function buildArticlePayload(draft: ArticleDraft): ArticleBuildResult {
    const errors: ArticleRuleError[] = [];
    const productName = draft.productName.trim();
    if (productName === '') errors.push({ field: 'productName', code: 'required' });
    else if (productName.length > MAX_NAME_LENGTH) errors.push({ field: 'productName', code: 'too_long' });
    const variantName = text(draft.variantName, MAX_NAME_LENGTH, 'variantName', errors);
    const shortcutCode = text(draft.shortcutCode, MAX_SHORTCUT_LENGTH, 'shortcutCode', errors);
    if (shortcutCode !== null && !SHORTCUT.test(shortcutCode)) errors.push({ field: 'shortcutCode', code: 'invalid' });
    const origin = text(draft.origin, MAX_NAME_LENGTH, 'origin', errors);
    const brand = text(draft.brand, MAX_NAME_LENGTH, 'brand', errors);
    const sizeCategory = text(draft.sizeCategory, MAX_NAME_LENGTH, 'sizeCategory', errors);
    const packaging = text(draft.packaging, MAX_NAME_LENGTH, 'packaging', errors);
    const packagingUnit = draft.packagingUnit === '' ? null : draft.packagingUnit;
    if (packagingUnit !== null && !(PACKAGE_UNITS as readonly string[]).includes(packagingUnit)) errors.push({ field: 'packagingUnit', code: 'invalid' });
    const season = text(draft.season, MAX_NAME_LENGTH, 'season', errors);
    const notes = text(draft.notes, MAX_NOTES_LENGTH, 'notes', errors);
    const weightMethod: WeightMethod = isWeightMethod(draft.weightMethod) ? draft.weightMethod : 'Weighed';
    if (!isWeightMethod(draft.weightMethod)) errors.push({ field: 'weightMethod', code: 'invalid' });
    const net = positive(draft.defaultNetKgPerPackage, 'defaultNetKgPerPackage', errors);
    const gross = positive(draft.defaultGrossKgPerPackage, 'defaultGrossKgPerPackage', errors);
    if (net !== null && gross !== null && gross < net) errors.push({ field: 'defaultGrossKgPerPackage', code: 'gross_below_net' });
    const perPallet = positive(draft.defaultPackagesPerPallet, 'defaultPackagesPerPallet', errors);

    // Goederencode als tekst met expliciet stelsel; lengte per stelsel (HS 6, CN 8, TARIC 10), voorloopnullen inbegrepen.
    const rawCode = draft.goodsCode.trim();
    const goodsCode = rawCode === '' ? null : goodsCodeDigits(rawCode);
    let goodsCodeSystem: GoodsCodeSystem | null = draft.goodsCodeSystem === '' ? null : draft.goodsCodeSystem;
    if (goodsCode !== null) {
        if (goodsCode === '' || goodsCode.length !== rawCode.replace(/[\s.]/g, '').length) errors.push({ field: 'goodsCode', code: 'invalid' });
        else if (goodsCodeSystem === null) errors.push({ field: 'goodsCodeSystem', code: 'system_required' });
        else if (!isGoodsCodeSystem(goodsCodeSystem)) errors.push({ field: 'goodsCodeSystem', code: 'invalid' });
        else if (goodsCode.length !== GOODS_CODE_LENGTH[goodsCodeSystem]) errors.push({ field: 'goodsCode', code: 'wrong_length' });
    } else {
        goodsCodeSystem = null; // een stelsel zonder code wordt niet vastgelegd
    }
    const tradeClass = text(draft.tradeClass, MAX_TRADE_CLASS_LENGTH, 'tradeClass', errors);

    if (errors.length > 0) return { ok: false, errors };
    return {
        ok: true,
        value: {
            productName, variantName, shortcutCode, origin, brand, sizeCategory, packaging, packagingUnit, season, notes, weightMethod,
            defaultNetKgPerPackage: net, defaultGrossKgPerPackage: gross, defaultPackagesPerPallet: perPallet,
            goodsCode: goodsCode === null ? null : goodsCode, goodsCodeSystem: goodsCode === null ? null : goodsCodeSystem, tradeClass,
        },
    };
}

export function groupArticleErrors(errors: ArticleRuleError[]): Record<string, ArticleRuleCode[]> {
    const grouped: Record<string, ArticleRuleCode[]> = {};
    for (const error of errors) (grouped[error.field] ??= []).push(error.code);
    return grouped;
}

function normalize(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

// Lijstfilter van de werkplek: elke term moet voorkomen in zoekcode, omschrijving, merk, herkomst, handelsklasse of
// goederencode. Een exacte zoekcode staat bovenaan.
export function searchArticles(articles: readonly Article[], query: string): Article[] {
    const term = normalize(query);
    if (term === '') return [...articles];
    const words = term.split(/\s+/).filter((word) => word !== '');
    const exact = (article: Article) => article.shortcutCode !== null && normalize(article.shortcutCode) === term;
    return articles
        .filter((article) => {
            const text = normalize([article.shortcutCode ?? '', article.description, article.brand ?? '', article.origin ?? '', article.tradeClass ?? '', article.goodsCode ?? '', `#${article.id}`].join(' '));
            return exact(article) || words.every((word) => text.includes(word));
        })
        .sort((a, b) => Number(exact(b)) - Number(exact(a)) || a.description.localeCompare(b.description));
}

export const GOODS_CODE_SYSTEM_OPTIONS = GOODS_CODE_SYSTEMS;
