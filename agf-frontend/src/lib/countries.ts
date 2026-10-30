import type { Country, CountryReference } from '../types/country';

// Landkiezer: zoeken op ISO-code ("PE"), Nederlandse en Engelse naam en eigen aliassen, zonder hoofdletters en accenten
// (spiegel van CountryRules.Key in de backend). Gekozen en opgeslagen wordt altijd het land; een alias is alleen een snelkoppeling.

export interface CountryMatch {
    country: Country;
    // De alias waarlangs gevonden is (om te tonen), anders null.
    alias: string | null;
    // Exacte code, naam of alias: een duidelijke treffer (Tab neemt hem zonder pijltjes).
    exact: boolean;
}

export const MAX_COUNTRY_SUGGESTIONS = 12;

export function countryKey(text: string): string {
    return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim().replace(/\s+/g, ' ');
}

export function countryName(reference: CountryReference | null, code: string | null): string {
    if (code === null) return '';
    return reference?.countries.find((country) => country.code === code)?.nameNl ?? code;
}

// Volgorde: exact (code, alias, naam), dan begint met, dan bevat; binnen een groep op Nederlandse naam.
export function searchCountries(reference: CountryReference, query: string, max = MAX_COUNTRY_SUGGESTIONS): CountryMatch[] {
    const term = countryKey(query);
    const byName = (a: CountryMatch, b: CountryMatch) => a.country.nameNl.localeCompare(b.country.nameNl, 'nl');
    if (term === '') return reference.countries.map((country) => ({ country, alias: null, exact: false })).sort(byName).slice(0, max);
    const aliasesByCode = new Map<string, string[]>();
    for (const alias of reference.aliases) aliasesByCode.set(alias.countryCode, [...(aliasesByCode.get(alias.countryCode) ?? []), alias.alias]);
    const ranked: { match: CountryMatch; rank: number }[] = [];
    for (const country of reference.countries) {
        const aliases = aliasesByCode.get(country.code) ?? [];
        const exactAlias = aliases.find((alias) => countryKey(alias) === term) ?? null;
        const names = [countryKey(country.nameNl), countryKey(country.nameEn)];
        if (countryKey(country.code) === term || exactAlias !== null || names.includes(term)) {
            ranked.push({ match: { country, alias: exactAlias, exact: true }, rank: 0 });
            continue;
        }
        const startAlias = aliases.find((alias) => countryKey(alias).startsWith(term)) ?? null;
        if (names.some((name) => name.startsWith(term)) || startAlias !== null) {
            ranked.push({ match: { country, alias: startAlias, exact: false }, rank: 1 });
            continue;
        }
        const containsAlias = aliases.find((alias) => countryKey(alias).includes(term)) ?? null;
        if (names.some((name) => name.includes(term)) || containsAlias !== null) {
            ranked.push({ match: { country, alias: containsAlias, exact: false }, rank: 2 });
        }
    }
    return ranked.sort((a, b) => a.rank - b.rank || byName(a.match, b.match)).slice(0, max).map((entry) => entry.match);
}
