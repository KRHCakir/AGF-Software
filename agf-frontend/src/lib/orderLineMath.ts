// Rekenregels van een orderregel met prijs, korting en btw (feedback 22 november), gedeeld door de inkooporder en de verkooporder.
// Spiegel van AgfAdmin/Services/OrderLineMath.cs; de server is leidend. Het bedrag is lineNet en de btw lineVat uit invoiceMath (per regel
// op centen, daarna opgeteld), zodat een factuur die de regels overneemt op de cent hetzelfde uitkomt.
// Pure module zonder React (node:test); imports met .ts-extensie.

import { lineNet, lineVat } from './invoiceMath.ts';
import { parseLocaleDecimal, type DecimalError, type NumberLocale } from './decimal.ts';

// De btw-tarieven van het artikel (zie ArticleRules.VatRates): 9% (standaard), 21% en 0%.
export const VAT_RATES = [9, 21, 0] as const;
export type VatRate = (typeof VAT_RATES)[number];
export const isVatRate = (value: number): value is VatRate => (VAT_RATES as readonly number[]).includes(value);

export function lineAmount(quantity: number | null, unitPrice: number | null, discountPercentage: number | null): number | null {
    return quantity === null || unitPrice === null ? null : lineNet(quantity, unitPrice, discountPercentage ?? 0);
}

export type DiscountError = DecimalError | 'out_of_range';

// Korting in procenten: leeg is geen korting; anders 0 tot en met 100 met hooguit 2 decimalen (nl-notatie: 2,5).
export function parseDiscount(text: string, locale?: NumberLocale): { ok: true; value: number | null } | { ok: false; error: DiscountError } {
    if (text.trim() === '') return { ok: true, value: null };
    const parsed = parseLocaleDecimal(text, locale, { maxDecimals: 2, maxDigits: 5, allowZero: true });
    if (!parsed.ok) return { ok: false, error: parsed.error };
    if (parsed.value < 0 || parsed.value > 100) return { ok: false, error: 'out_of_range' };
    return { ok: true, value: parsed.value };
}

export interface AmountLine {
    amount: number | null;
    vatPercentage: number | null;
}

export interface VatRateTotal {
    percentage: number;
    net: number;
    vat: number;
}

// Zoals OrderAmountsDto van de server: totaal excl. btw, btw per tarief, totaal btw en incl. btw; regels zonder prijs en regels met een
// bedrag maar zonder btw-tarief (opgeslagen vóór deze ronde) apart geteld.
export interface OrderAmounts {
    net: number | null;
    vatRates: VatRateTotal[];
    vat: number | null;
    gross: number | null;
    linesWithoutPrice: number;
    linesWithoutVat: number;
}

const cents = (value: number) => Math.round(value * 100);

export function summarize(lines: readonly AmountLine[]): OrderAmounts {
    const priced = lines.filter((line): line is { amount: number; vatPercentage: number | null } => line.amount !== null);
    const byRate = new Map<number, { net: number; vat: number }>();
    for (const line of priced) {
        if (line.vatPercentage === null) continue;
        const current = byRate.get(line.vatPercentage) ?? { net: 0, vat: 0 };
        byRate.set(line.vatPercentage, { net: current.net + cents(line.amount), vat: current.vat + cents(lineVat(line.amount, line.vatPercentage)) });
    }
    const vatRates = [...byRate.entries()].sort(([a], [b]) => a - b).map(([percentage, sum]) => ({ percentage, net: sum.net / 100, vat: sum.vat / 100 }));
    const netCents = priced.reduce((total, line) => total + cents(line.amount), 0);
    const vatCents = vatRates.reduce((total, rate) => total + cents(rate.vat), 0);
    return {
        net: priced.length === 0 ? null : netCents / 100,
        vatRates,
        vat: priced.length === 0 ? null : vatCents / 100,
        gross: priced.length === 0 ? null : (netCents + vatCents) / 100,
        linesWithoutPrice: lines.length - priced.length,
        linesWithoutVat: priced.filter((line) => line.vatPercentage === null).length,
    };
}

// Een percentage voor op het scherm (9; 2,5): hooguit 2 decimalen, in de notatie van de schermtaal, zonder het %-teken.
export function formatPercent(value: number, locale: string): string {
    return new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { maximumFractionDigits: 2 }).format(value);
}
