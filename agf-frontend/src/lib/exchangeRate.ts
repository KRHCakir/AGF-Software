// Koersen bij facturen in een andere valuta (gespiegeld van AgfAdmin/Services/ExchangeRates/ExchangeRateRules.cs): de ECB-koers in
// eenheden valuta per 1 euro (1 EUR = 1,1165 USD). Kosten op partijen staan altijd in euro; de server rekent het regelbedrag om en
// bewaakt de grens. Hier alleen weergave. Geen imports: met node:test te toetsen.

export const EURO = 'EUR';

export function isEuro(currency: string | null | undefined): boolean {
    return currency === null || currency === undefined || currency.trim() === '' || currency.trim().toUpperCase() === EURO;
}

// Op centen, half van nul af (zoals decimal.Round(..., AwayFromZero) op de server).
function cents(value: number): number {
    const scaled = Math.abs(value) * 100;
    const rounded = Math.round(Number(scaled.toFixed(6)));
    return (Math.sign(value) * rounded) / 100;
}

// Bedrag in valuta naar euro (alleen een indicatie in het invoerscherm; de server rekent zelf per regel).
export function toEuro(amount: number, unitsPerEuro: number): number {
    return cents(amount / unitsPerEuro);
}

// Het deel van de regel in de valuta van de factuur, om het oorspronkelijke bedrag naast het eurobedrag te tonen: naar verhouding van de
// regel, zodat de hele regel precies het regelbedrag is (geen afrondingscent heen en terug). Gelijk aan InCurrency op de server.
export function inCurrency(euro: number, lineTotal: number, lineEuro: number): number {
    return lineEuro <= 0 ? 0 : cents((lineTotal * euro) / lineEuro);
}

// 1,1165 / 175,68: zoveel decimalen als de ECB geeft (hoogstens zes), in de notatie van de schermtaal.
export function formatRate(unitsPerEuro: number, locale: string): string {
    return unitsPerEuro.toLocaleString(locale === 'en' ? 'en-GB' : 'nl-NL', { minimumFractionDigits: 2, maximumFractionDigits: 6 });
}
