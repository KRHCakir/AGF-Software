// Valuta's als suggestie in valutavelden (inkoopfactuur, instellingen, relaties). Volgorde = hoe vaak ze in de AGF-handel voorkomen:
// EUR eerst, dan USD en GBP, dan de landen waar AGF vandaan komt. Een andere code van drie letters mag ook (de server controleert
// alleen de vorm); dit is alleen een hulp bij het typen. Geen imports: met node:test te toetsen.

export interface CurrencyOption {
    code: string;
    nl: string;
    en: string;
}

export const CURRENCIES: readonly CurrencyOption[] = [
    { code: 'EUR', nl: 'Euro', en: 'Euro' },
    { code: 'USD', nl: 'Amerikaanse dollar', en: 'US dollar' },
    { code: 'GBP', nl: 'Britse pond', en: 'Pound sterling' },
    { code: 'CHF', nl: 'Zwitserse frank', en: 'Swiss franc' },
    { code: 'ZAR', nl: 'Zuid-Afrikaanse rand', en: 'South African rand' },
    { code: 'PLN', nl: 'Poolse zloty', en: 'Polish zloty' },
    { code: 'TRY', nl: 'Turkse lira', en: 'Turkish lira' },
    { code: 'MAD', nl: 'Marokkaanse dirham', en: 'Moroccan dirham' },
    { code: 'CZK', nl: 'Tsjechische kroon', en: 'Czech koruna' },
    { code: 'DKK', nl: 'Deense kroon', en: 'Danish krone' },
    { code: 'SEK', nl: 'Zweedse kroon', en: 'Swedish krona' },
    { code: 'NOK', nl: 'Noorse kroon', en: 'Norwegian krone' },
    { code: 'HUF', nl: 'Hongaarse forint', en: 'Hungarian forint' },
    { code: 'RON', nl: 'Roemeense leu', en: 'Romanian leu' },
    { code: 'CAD', nl: 'Canadese dollar', en: 'Canadian dollar' },
    { code: 'AUD', nl: 'Australische dollar', en: 'Australian dollar' },
    { code: 'NZD', nl: 'Nieuw-Zeelandse dollar', en: 'New Zealand dollar' },
    { code: 'BRL', nl: 'Braziliaanse real', en: 'Brazilian real' },
    { code: 'CLP', nl: 'Chileense peso', en: 'Chilean peso' },
    { code: 'PEN', nl: 'Peruaanse sol', en: 'Peruvian sol' },
    { code: 'COP', nl: 'Colombiaanse peso', en: 'Colombian peso' },
    { code: 'MXN', nl: 'Mexicaanse peso', en: 'Mexican peso' },
    { code: 'ARS', nl: 'Argentijnse peso', en: 'Argentine peso' },
    { code: 'EGP', nl: 'Egyptisch pond', en: 'Egyptian pound' },
    { code: 'ILS', nl: 'Israëlische sjekel', en: 'Israeli shekel' },
    { code: 'KES', nl: 'Keniaanse shilling', en: 'Kenyan shilling' },
    { code: 'INR', nl: 'Indiase roepie', en: 'Indian rupee' },
    { code: 'CNY', nl: 'Chinese yuan', en: 'Chinese yuan' },
    { code: 'JPY', nl: 'Japanse yen', en: 'Japanese yen' },
];

export const MAX_CURRENCY_SUGGESTIONS = 8;

// Wat je in een valutaveld typt: alleen letters, hoofdletters, hoogstens drie.
export function cleanCurrencyInput(text: string): string {
    return text.toUpperCase().replace(/[^A-Z]/g, '').slice(0, 3);
}

// Suggesties voor wat er getypt is: eerst codes die ermee beginnen (E geeft EUR, dan EGP), daarna valuta's waarvan de naam het
// bevat (DOL geeft de dollars). Leeg: de hele lijst (bij pijltje omlaag).
export function suggestCurrencies(query: string, locale: string, max = MAX_CURRENCY_SUGGESTIONS): CurrencyOption[] {
    const code = cleanCurrencyInput(query);
    if (code === '') return CURRENCIES.slice(0, max);
    const byCode = CURRENCIES.filter((option) => option.code.startsWith(code));
    const name = code.toLowerCase();
    const byName = code.length < 2 ? [] : CURRENCIES.filter((option) => !byCode.includes(option)
        && (locale === 'en' ? option.en : option.nl).toLowerCase().includes(name));
    return [...byCode, ...byName].slice(0, max);
}

export function currencyName(option: CurrencyOption, locale: string): string {
    return locale === 'en' ? option.en : option.nl;
}
