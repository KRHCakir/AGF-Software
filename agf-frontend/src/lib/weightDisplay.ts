// Gewichten tonen (fixes 5a). Totalen van gewichten (netto of bruto kg van een regel, een order, een bon) staan als hele kilo's,
// of met één decimaal als het bedrijf dat instelt (Instellingen › Gewichten tonen). Afronden gebeurt alleen bij het tonen, halve
// kilo's omhoog (2.272,5 wordt 2.273); wat wordt opgeslagen en verstuurd blijft de echte waarde met drie decimalen. Een totaal
// onder regels is de som van de getoonde regels, zodat het optelt. Het gewicht per colli houdt zijn decimalen (1,50 kg).

// De instelling van het bedrijf, gezet door de WeightDisplayProvider; zo tonen pure functies zonder extra parameter hetzelfde.
let decimals = 0;

export function setWeightDecimals(value: number) {
    decimals = value === 1 ? 1 : 0;
}

export function weightDecimals(): number {
    return decimals;
}

const numberLocale = (locale: string) => (locale === 'nl' ? 'nl-NL' : 'en-GB');

// Naar het getoonde aantal decimalen, half omhoog. De kleine marge vangt binaire breuken op (waarden hebben hooguit 3 decimalen).
export function roundWeight(kg: number): number {
    const factor = 10 ** decimals;
    return Math.round(kg * factor + 1e-7) / factor;
}

// "2.273" (of "2.272,5" met één decimaal), zonder eenheid.
export function formatWeight(kg: number, locale: string): string {
    return new Intl.NumberFormat(numberLocale(locale), { minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(roundWeight(kg));
}

export function formatKg(kg: number, locale: string): string {
    return `${formatWeight(kg, locale)} kg`;
}

// Gewicht per colli of verpakking: minstens twee decimalen (4,00; 1,50; 1,125), want daar zijn decimalen juist nodig.
export function formatPerPackage(kg: number, locale: string): string {
    return new Intl.NumberFormat(numberLocale(locale), { minimumFractionDigits: 2, maximumFractionDigits: 3 }).format(kg);
}

export function formatKgPerPackage(kg: number, locale: string): string {
    return `${formatPerPackage(kg, locale)} kg`;
}

// Het totaal zoals het optelt: de som van de regels zoals ze er staan; regels zonder gewicht tellen niet mee. Een regel staat
// afgerond (hele kilo's of één decimaal), behalve een zelf ingevuld gewicht in een invoerveld: dat staat er zoals getypt
// (2.020,40) en telt dus exact mee (`exact`). Het totaal zelf wordt daarna getoond als hele kilo's of met één decimaal.
export function shownKgTotal(values: readonly (number | null | undefined)[], exact?: readonly boolean[]): number {
    const sum = values.reduce<number>((total, kg, index) => (kg === null || kg === undefined ? total : total + (exact?.[index] ? kg : roundWeight(kg))), 0);
    return roundWeight(Math.round(sum * 1000) / 1000);
}
