// Emballage (fust): pure regels voor node:test (imports met .ts-extensie), spiegel van SalesOrderService.Packaging.cs. Geen voorraad:
// geen eenheid, kg, bron of reservering.
import type { CostArticle } from '../types/costArticle';
import type { CreateSalesOrderPackagingLineRequest } from '../types/salesOrder';
import { formatAmountInput, parseLocaleDecimal, type DecimalError, type NumberLocale } from './decimal.ts';
import type { RuleError } from './salesOrderRules';

export const MAX_PACKAGING_QUANTITY = 99999;
export const MAX_PACKAGING_LINES = 50;
// Standaardartikel voor "Pallet toevoegen" (migratie AddPackagingArticles).
export const DISPOSABLE_PALLET = 'Pallet wegwerp';

export interface PackagingLineDraft {
    key: string;
    costArticleId: string;
    quantity: string;
    unitPrice: string;
}

export type PriceResult = { ok: true; value: number | null } | { ok: false; error: DecimalError };

// Prijs per stuk excl. btw: leeg = (nog) geen prijs; anders 0 of meer met hoogstens 2 decimalen. Zonder locale: de schermtaal
// (setInputLocale, gezet door de LanguageProvider), zoals bij alle getalvelden.
export function parseUnitPrice(text: string, locale?: NumberLocale): PriceResult {
    if (text.trim() === '') return { ok: true, value: null };
    const parsed = parseLocaleDecimal(text, locale, { maxDecimals: 2, maxDigits: 16, allowZero: true });
    return parsed.ok ? { ok: true, value: parsed.value } : parsed;
}

// Heel aantal van 1 tot en met 99.999 ("1.000" mag in het Nederlands); null bij ongeldige invoer.
export function parsePackagingQuantity(text: string, locale?: NumberLocale): number | null {
    const parsed = parseLocaleDecimal(text, locale, { maxDecimals: 0, maxDigits: 5 });
    return parsed.ok && parsed.value <= MAX_PACKAGING_QUANTITY ? parsed.value : null;
}

// Alle emballageregels tegelijk, met dezelfde veldsleutels als de backend (packagingLines[0].quantity).
export function buildPackagingLines(lines: PackagingLineDraft[], locale?: NumberLocale): { ok: true; value: CreateSalesOrderPackagingLineRequest[] } | { ok: false; errors: RuleError[] } {
    const errors: RuleError[] = [];
    if (lines.length > MAX_PACKAGING_LINES) errors.push({ field: 'packagingLines', code: 'too_many_lines' });
    const value = lines.map((line, index) => {
        const prefix = `packagingLines[${index}]`;
        if (!/^[1-9]\d*$/.test(line.costArticleId)) errors.push({ field: `${prefix}.costArticleId`, code: 'required' });
        const quantity = parsePackagingQuantity(line.quantity, locale);
        if (quantity === null) errors.push({ field: `${prefix}.quantity`, code: line.quantity.trim() === '' ? 'required' : 'not_whole' });
        const price = parseUnitPrice(line.unitPrice, locale);
        if (!price.ok) errors.push({ field: `${prefix}.unitPrice`, code: price.error });
        return { costArticleId: Number(line.costArticleId), quantity: quantity ?? 0, unitPrice: price.ok ? price.value : null };
    });
    return errors.length > 0 ? { ok: false, errors } : { ok: true, value };
}

// Aantal × prijs (excl. btw) voor in beeld; null zolang aantal of prijs ontbreekt of ongeldig is.
export function packagingLineTotal(line: PackagingLineDraft, locale?: NumberLocale): number | null {
    const quantity = parsePackagingQuantity(line.quantity, locale);
    const price = parseUnitPrice(line.unitPrice, locale);
    return quantity === null || !price.ok || price.value === null ? null : Math.round(quantity * price.value * 100) / 100;
}

// Voorgevulde prijs bij een gekozen artikel: de standaardprijs met twee decimalen, of leeg.
export function defaultPriceText(article: CostArticle | undefined, locale?: NumberLocale): string {
    return article?.defaultUnitPrice == null ? '' : formatAmountInput(article.defaultUnitPrice, locale);
}

// Voorstel voor "Pallet toevoegen": de "= x pallets" van de goederenregels opgeteld, naar boven afgerond; minimaal 1.
export function palletProposal(lines: { quantity: number | null; packagesPerPallet: number | null | undefined }[]): number {
    const pallets = lines.reduce((sum, line) => sum + (line.quantity !== null && line.quantity > 0 && line.packagesPerPallet ? line.quantity / line.packagesPerPallet : 0), 0);
    return Math.max(1, Math.ceil(pallets - 1e-9));
}

// Verpakkingen per pallet van een goederenregel: bij palletinvoer het getypte aantal, anders de standaard van de variant.
export function effectivePackagesPerPallet(line: { mode: string; packagesPerPallet: string }, variantDefault: number | null | undefined): number | null | undefined {
    if (line.mode === 'pallets') {
        const typed = parseLocaleDecimal(line.packagesPerPallet);
        if (typed.ok && Number.isInteger(typed.value)) return typed.value;
    }
    return variantDefault;
}

// Het artikel voor "Pallet toevoegen": Pallet wegwerp, anders het eerste actieve artikel met "pallet" in de naam; null als er geen is.
export function palletArticle(articles: CostArticle[]): CostArticle | null {
    const active = articles.filter((article) => article.isActive && article.kind !== 'Cost');
    return active.find((article) => article.name.trim().toLowerCase() === DISPOSABLE_PALLET.toLowerCase())
        ?? active.find((article) => article.name.toLowerCase().includes('pallet')) ?? null;
}

// Een bevroren regel (onafgerond commando) terug als invoer.
export function packagingDraftFrom(line: CreateSalesOrderPackagingLineRequest, key: string, locale?: NumberLocale): PackagingLineDraft {
    return { key, costArticleId: String(line.costArticleId), quantity: String(line.quantity), unitPrice: line.unitPrice === null ? '' : formatAmountInput(line.unitPrice, locale) };
}
