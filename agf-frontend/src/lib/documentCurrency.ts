// Valuta en koers van een inkoopdocument (valuta-ronde, november 2026), gespiegeld van AgfAdmin/Services/ExchangeRates/DocumentCurrency.cs.
// De boekvaluta is euro. De koers is eenheden valuta per 1 euro, zoals de ECB noteert: 1 EUR = 1,0842 USD. Het bedrag in euro is het
// bedrag gedeeld door de koers, op centen (toEuro). De ECB-koers haalt de server op; past de gebruiker hem aan, dan is de bron Manual
// ("Handmatig") en vraagt het scherm daarna nergens meer om bevestiging. Pure module zonder React (node:test); imports met .ts-extensie.

import { parseLocaleDecimal } from './decimal.ts';
import { EURO, isEuro, toEuro } from './exchangeRate.ts';

export type RateSource = 'ECB' | 'Manual' | 'NotApplicable';

export interface CurrencyDraft {
    currency: string;
    // De koers als invoertekst in de schermtaal (1,0842); leeg zolang er geen koers is.
    rate: string;
    source: RateSource;
    // Publicatiedatum van de ECB-koers (yyyy-MM-dd); null bij een handmatige koers of euro.
    rateDate: string | null;
    // De gebruiker koos de valuta zelf: een andere leverancier stelt dan geen valuta meer voor.
    touched: boolean;
}

export const euroDraft = (): CurrencyDraft => ({ currency: EURO, rate: '', source: 'NotApplicable', rateDate: null, touched: false });

export function isRateSource(value: unknown): value is RateSource {
    return value === 'ECB' || value === 'Manual' || value === 'NotApplicable';
}

const numberLocale = (locale: string) => (locale === 'en' ? 'en-GB' : 'nl-NL');

// De koers als invoertekst: zoveel decimalen als hij heeft (hoogstens zes), in de schermtaal.
export function rateText(unitsPerEuro: number, locale: string): string {
    return unitsPerEuro.toLocaleString(numberLocale(locale), { minimumFractionDigits: 2, maximumFractionDigits: 6, useGrouping: false });
}

// "1 EUR = 1,0842 USD", de notatie van de ECB.
export function rateLine(currency: string, unitsPerEuro: number, locale: string): string {
    return `1 EUR = ${rateText(unitsPerEuro, locale)} ${currency}`;
}

// De andere richting, als uitleg: "1 USD = 0,9223 EUR" (vier decimalen).
export function inverseLine(currency: string, unitsPerEuro: number, locale: string): string {
    const inverse = (1 / unitsPerEuro).toLocaleString(numberLocale(locale), { minimumFractionDigits: 4, maximumFractionDigits: 4 });
    return `1 ${currency} = ${inverse} EUR`;
}

// Een eurobedrag als "€ 8.077,00" (nl) of "€8,077.00" (en): naast of onder een bedrag in een andere valuta.
export function formatEuro(value: number, locale: string): string {
    return new Intl.NumberFormat(numberLocale(locale), { style: 'currency', currency: EURO, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}

export type RateCheck = { ok: true; rate: number } | { ok: false };

// Een handmatige koers: positief, hoogstens zes decimalen (zoals ExchangeRateRules.IsRate).
export function parseRate(text: string): RateCheck {
    // Hoogstens zes decimalen: de parser weigert er meer (geen stille afronding).
    const parsed = parseLocaleDecimal(text, undefined, { maxDecimals: 6 });
    return parsed.ok && parsed.value !== null && parsed.value > 0 ? { ok: true, rate: parsed.value } : { ok: false };
}

// De koers waarmee het scherm rekent: 1 bij euro, de ingevulde of opgehaalde koers anders; null zolang er geen geldige koers is.
export function effectiveRate(draft: CurrencyDraft): number | null {
    if (isEuro(draft.currency)) return 1;
    const check = parseRate(draft.rate);
    return check.ok ? check.rate : null;
}

// Het bedrag in euro, of null zonder koers.
export function amountInEuro(amount: number | null, draft: CurrencyDraft): number | null {
    const rate = effectiveRate(draft);
    return amount === null || rate === null ? null : isEuro(draft.currency) ? amount : toEuro(amount, rate);
}

export interface CurrencyPayload {
    currency: string;
    exchangeRate: number | null;
    // Alleen bij een andere valuta dan euro: ECB of Manual.
    exchangeRateSource: 'ECB' | 'Manual' | null;
}

export type CurrencyPayloadResult = { ok: true; value: CurrencyPayload } | { ok: false; field: 'currency' | 'exchangeRate' };

// Wat er naar de server gaat. Bij ECB stuurt het scherm de getoonde koers mee, maar de server legt zelf vast (dezelfde dagkoers). Zonder
// koers kan opslaan niet: bij ECB loopt het ophalen dan nog (het scherm toont geen eurobedrag van een vorige koers), bij Handmatig is hij leeg.
export function currencyPayload(draft: CurrencyDraft): CurrencyPayloadResult {
    const currency = draft.currency.trim().toUpperCase();
    if (!/^[A-Z]{3}$/.test(currency)) return { ok: false, field: 'currency' };
    if (currency === EURO) return { ok: true, value: { currency, exchangeRate: null, exchangeRateSource: null } };
    const check = parseRate(draft.rate);
    const source = draft.source;
    if (source === 'NotApplicable' || !check.ok) return { ok: false, field: 'exchangeRate' };
    return { ok: true, value: { currency, exchangeRate: check.ok ? check.rate : null, exchangeRateSource: source } };
}

// Een opgeslagen document als invoer (bij aanpassen): de vastgelegde koers zoals hij is.
export function draftFromDocument(currency: string | null | undefined, rate: number | null | undefined, source: string | null | undefined, rateDate: string | null | undefined, locale: string): CurrencyDraft {
    if (isEuro(currency)) return euroDraft();
    return {
        currency: (currency ?? EURO).toUpperCase(), rate: rate == null ? '' : rateText(rate, locale), source: source === 'Manual' ? 'Manual' : 'ECB',
        rateDate: rateDate ?? null, touched: true,
    };
}
