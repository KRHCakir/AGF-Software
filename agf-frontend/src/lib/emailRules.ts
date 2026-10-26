// Strikte e-mailvorm voor nieuwe of gewijzigde adressen, spiegel van AgfAdmin/Services/EmailRules.cs (StrictPattern en TypoDomains
// exact). Bestaande, ongewijzigde waarden worden niet opnieuw getoetst. Een bekende tikfout in het domein is een suggestie
// (waarschuwing om aan te klikken), geen fout; de controle op een mailserver (MX) doet alleen de server bij opslaan.

export const STRICT_EMAIL = /^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$/;
export const MAX_EMAIL_LENGTH = 254;
export const MAX_EMAIL_LOCAL_LENGTH = 64;

// Tikfout in het domein -> bedoeld domein (kleine letters, exacte vergelijking op het hele domein).
export const TYPO_DOMAINS: Readonly<Record<string, string>> = {
    'gmial.com': 'gmail.com', 'gmai.com': 'gmail.com', 'gamil.com': 'gmail.com', 'gmal.com': 'gmail.com', 'gnail.com': 'gmail.com',
    'gmaill.com': 'gmail.com', 'gmail.co': 'gmail.com', 'gmail.con': 'gmail.com', 'gmail.cm': 'gmail.com', 'gmail.om': 'gmail.com',
    'hotmial.com': 'hotmail.com', 'hotmai.com': 'hotmail.com', 'hotmal.com': 'hotmail.com', 'hotmil.com': 'hotmail.com', 'homail.com': 'hotmail.com',
    'hotmail.co': 'hotmail.com', 'hotmail.con': 'hotmail.com', 'hotmial.nl': 'hotmail.nl', 'hotmail.nll': 'hotmail.nl',
    'outlok.com': 'outlook.com', 'outllok.com': 'outlook.com', 'outloo.com': 'outlook.com', 'outlook.co': 'outlook.com', 'outlook.con': 'outlook.com',
    'outlok.nl': 'outlook.nl',
    'yahooo.com': 'yahoo.com', 'yaho.com': 'yahoo.com', 'yahoo.co': 'yahoo.com', 'yahoo.con': 'yahoo.com',
    'iclod.com': 'icloud.com', 'icloud.co': 'icloud.com', 'icloud.con': 'icloud.com',
    'live.nll': 'live.nl', 'live.co': 'live.com',
    'ziggo.nll': 'ziggo.nl', 'zigo.nl': 'ziggo.nl', 'ziggo.n': 'ziggo.nl',
    'kpnmial.nl': 'kpnmail.nl', 'kpnmail.nll': 'kpnmail.nl', 'kpnmal.nl': 'kpnmail.nl',
    'xs4al.nl': 'xs4all.nl', 'xs4all.nll': 'xs4all.nl',
    'hetnet.nll': 'hetnet.nl', 'planet.nll': 'planet.nl', 'telfort.nll': 'telfort.nl',
    't-onlin.de': 't-online.de', 't-online.d': 't-online.de', 'gmx.d': 'gmx.de', 'web.d': 'web.de',
};

export function isStrictEmail(value: string): boolean {
    return value.length <= MAX_EMAIL_LENGTH && STRICT_EMAIL.test(value) && value.indexOf('@') <= MAX_EMAIL_LOCAL_LENGTH;
}

export function emailDomain(value: string): string | null {
    const at = value.lastIndexOf('@');
    return at < 0 || at === value.length - 1 ? null : value.slice(at + 1).toLowerCase();
}

// Suggestie bij een bekende tikfout (zelfde lokaal deel, verbeterd domein), anders null.
export function emailTypoSuggestion(value: string): string | null {
    const trimmed = value.trim();
    const domain = emailDomain(trimmed);
    if (domain === null || !Object.prototype.hasOwnProperty.call(TYPO_DOMAINS, domain)) return null;
    return `${trimmed.slice(0, trimmed.lastIndexOf('@'))}@${TYPO_DOMAINS[domain]}`;
}
