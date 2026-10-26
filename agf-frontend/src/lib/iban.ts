// IBAN- en BIC-regels, spiegel van AgfAdmin/Services/BankAccountRules.cs (lengtetabel, mod-97, BIC-vorm). Een geldige IBAN bewijst
// alleen vorm en controlegetal, niet dat de rekening bestaat of van de relatie is: daarom blijft elke nieuwe of gewijzigde
// rekening "niet bevestigd" tot iemand haar bevestigt.

export const MAX_ACCOUNT_HOLDER_LENGTH = 200;
export const MAX_OTHER_ACCOUNT_NUMBER_LENGTH = 64;
export const MAX_BANK_ACCOUNTS = 20;

// IBAN-lengte per land (SWIFT IBAN-register). Een land buiten deze tabel wordt geweigerd: gebruik dan "ander rekeningnummer".
export const IBAN_LENGTHS: Readonly<Record<string, number>> = {
    AD: 24, AE: 23, AL: 28, AT: 20, AZ: 28, BA: 20, BE: 16, BG: 22, BH: 22, BI: 27,
    BR: 29, BY: 28, CH: 21, CR: 22, CY: 28, CZ: 24, DE: 22, DJ: 27, DK: 18, DO: 28,
    EE: 20, EG: 29, ES: 24, FI: 18, FK: 18, FO: 18, FR: 27, GB: 22, GE: 22, GI: 23,
    GL: 18, GR: 27, GT: 28, HN: 28, HR: 21, HU: 28, IE: 22, IL: 23, IQ: 23, IS: 26,
    IT: 27, JO: 30, KW: 30, KZ: 20, LB: 28, LC: 32, LI: 21, LT: 20, LU: 20, LV: 21,
    LY: 25, MC: 27, MD: 24, ME: 22, MK: 19, MN: 20, MR: 27, MT: 31, MU: 30, NI: 28,
    NL: 18, NO: 15, OM: 23, PK: 24, PL: 28, PS: 29, PT: 25, QA: 29, RO: 24, RS: 22,
    RU: 33, SA: 24, SC: 31, SD: 18, SE: 24, SI: 19, SK: 24, SM: 27, SO: 23, ST: 25,
    SV: 28, TL: 23, TN: 24, TR: 26, UA: 29, VA: 22, VG: 24, XK: 20, YE: 30,
};

const IBAN_SHAPE = /^[A-Z]{2}[0-9]{2}[A-Z0-9]+$/;
// BIC: 4 letters bank, 2 letters land, 2 tekens locatie, optioneel 3 tekens filiaal (AAAABBCC of AAAABBCCDDD).
const BIC_SHAPE = /^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$/;

// Normaliseren: alle witruimte (ook niet-afbrekend) weg, hoofdletters. Leeg -> null.
export function normalizeIban(raw: string): string | null {
    const value = raw.replace(/\s/g, '').toUpperCase();
    return value === '' ? null : value;
}

export const normalizeBic = normalizeIban;

export type IbanProblem = 'none' | 'shape' | 'unknown_country' | 'length' | 'checksum';

// ISO 7064 mod 97-10: eerste vier tekens naar achteren, letters A=10 … Z=35, rest bij deling door 97 (stapsgewijs).
export function mod97(iban: string): number {
    const rearranged = iban.slice(4) + iban.slice(0, 4);
    let remainder = 0;
    for (const c of rearranged) {
        const value = c >= '0' && c <= '9' ? c.charCodeAt(0) - 48 : c.charCodeAt(0) - 55;
        remainder = value >= 10 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
    }
    return remainder;
}

// Controle in vaste volgorde: vorm, land bekend, lengte voor dat land, mod-97 = 1.
export function checkIban(normalized: string | null): IbanProblem {
    if (normalized === null || !IBAN_SHAPE.test(normalized)) return 'shape';
    const length = IBAN_LENGTHS[normalized.slice(0, 2)];
    if (length === undefined) return 'unknown_country';
    if (normalized.length !== length) return 'length';
    return mod97(normalized) === 1 ? 'none' : 'checksum';
}

export const isValidIban = (normalized: string | null): boolean => checkIban(normalized) === 'none';
export const isBic = (normalized: string | null): boolean => normalized !== null && BIC_SHAPE.test(normalized);

// Leesbare weergave in groepen van vier (alleen tonen; opgeslagen wordt de genormaliseerde vorm).
export function formatIban(iban: string): string {
    return iban.replace(/(.{4})(?=.)/g, '$1 ');
}
