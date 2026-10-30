import { isArrayOf, isNullableString, isNumber, isObject, isString } from './guards';

// Contract van api/countries en api/articles/origin-cleanup (backend: CountryDtos.cs, ArticleDtos.cs). Herkomst is altijd een land
// uit de vaste lijst; een alias is alleen een snelkoppeling bij het zoeken.

export interface Country {
    code: string;
    nameNl: string;
    nameEn: string;
}

export interface CountryAlias {
    id: number;
    alias: string;
    countryCode: string;
}

export interface CountryReference {
    countries: Country[];
    aliases: CountryAlias[];
}

export interface OriginCleanupItem {
    id: number;
    description: string;
    shortcutCode: string | null;
    origin: string | null;
    suggestions: string[];
}

export function isCountryAlias(value: unknown): value is CountryAlias {
    return isObject(value) && isNumber(value.id) && isString(value.alias) && isString(value.countryCode);
}

export function isCountryReference(value: unknown): value is CountryReference {
    return isObject(value)
        && isArrayOf(value.countries, (item): item is Country => isObject(item) && isString(item.code) && isString(item.nameNl) && isString(item.nameEn))
        && isArrayOf(value.aliases, isCountryAlias);
}

export function isOriginCleanupList(value: unknown): value is OriginCleanupItem[] {
    return isArrayOf(value, (item): item is OriginCleanupItem => isObject(item) && isNumber(item.id) && isString(item.description)
        && isNullableString(item.shortcutCode) && isNullableString(item.origin) && isArrayOf(item.suggestions, isString));
}
