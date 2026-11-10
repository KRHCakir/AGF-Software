// Emballage (fust): pallets en kratten met een prijs per stuk. Pure regels zonder React of i18n, zodat ze met node:test te toetsen zijn;
// spiegel van AgfAdmin/Services/CostArticleRules.cs. Let op: imports met .ts-extensie zijn nodig voor node:test.
import { parseLocaleDecimal, type DecimalError, type NumberLocale } from './decimal.ts';

export type PriceResult = { ok: true; value: number | null } | { ok: false; error: DecimalError };

// Prijs per stuk excl. btw: leeg = (nog) geen prijs; anders 0 of meer met hoogstens 2 decimalen, in de notatie van de schermtaal.
export function parseUnitPrice(text: string, locale?: NumberLocale): PriceResult {
    if (text.trim() === '') return { ok: true, value: null };
    const parsed = parseLocaleDecimal(text, locale, { maxDecimals: 2, maxDigits: 16, allowZero: true });
    return parsed.ok ? { ok: true, value: parsed.value } : parsed;
}
