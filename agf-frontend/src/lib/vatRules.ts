// Btw-nummervorm per EU-lidstaat (plus XI voor Noord-Ierland), spiegel van AgfAdmin/Services/VatRules.cs, op het genormaliseerde
// nummer (normalizeVat: hoofdletters, zonder spaties/punten/streepjes, inclusief landprefix). Griekenland gebruikt EL; GR wordt
// geweigerd met de suggestie EL. Een land buiten de tabel valt terug op de algemene vorm (isVatShape). Alleen vormcontrole, geen
// VIES. Geldt alleen voor nieuwe of gewijzigde nummers.

export const VAT_PATTERNS: Readonly<Record<string, RegExp>> = {
    AT: /^ATU[0-9]{8}$/,
    BE: /^BE[01][0-9]{9}$/,
    BG: /^BG[0-9]{9,10}$/,
    CY: /^CY[0-9]{8}[A-Z]$/,
    CZ: /^CZ[0-9]{8,10}$/,
    DE: /^DE[0-9]{9}$/,
    DK: /^DK[0-9]{8}$/,
    EE: /^EE[0-9]{9}$/,
    EL: /^EL[0-9]{9}$/,
    ES: /^ES[A-Z0-9][0-9]{7}[A-Z0-9]$/,
    FI: /^FI[0-9]{8}$/,
    FR: /^FR[A-HJ-NP-Z0-9]{2}[0-9]{9}$/,
    HR: /^HR[0-9]{11}$/,
    HU: /^HU[0-9]{8}$/,
    IE: /^IE([0-9]{7}[A-W][A-IW]?|[0-9][A-Z][0-9]{5}[A-W])$/,
    IT: /^IT[0-9]{11}$/,
    LT: /^LT([0-9]{9}|[0-9]{12})$/,
    LU: /^LU[0-9]{8}$/,
    LV: /^LV[0-9]{11}$/,
    MT: /^MT[0-9]{8}$/,
    NL: /^NL[0-9]{9}B[0-9]{2}$/,
    PL: /^PL[0-9]{10}$/,
    PT: /^PT[0-9]{9}$/,
    RO: /^RO[1-9][0-9]{1,9}$/,
    SE: /^SE[0-9]{10}01$/,
    SI: /^SI[0-9]{8}$/,
    SK: /^SK[0-9]{10}$/,
    XI: /^XI([0-9]{9}|[0-9]{12}|GD[0-4][0-9]{2}|HA[5-9][0-9]{2})$/,
};

// Voorbeeld per land voor de foutmelding.
export const VAT_EXAMPLES: Readonly<Record<string, string>> = {
    AT: 'ATU12345678', BE: 'BE0123456789', BG: 'BG123456789', CY: 'CY12345678X', CZ: 'CZ12345678',
    DE: 'DE123456789', DK: 'DK12345678', EE: 'EE123456789', EL: 'EL123456789', ES: 'ESX1234567X',
    FI: 'FI12345678', FR: 'FRXX123456789', HR: 'HR12345678901', HU: 'HU12345678', IE: 'IE1234567X',
    IT: 'IT12345678901', LT: 'LT123456789', LU: 'LU12345678', LV: 'LV12345678901', MT: 'MT12345678',
    NL: 'NL123456789B01', PL: 'PL1234567890', PT: 'PT123456789', RO: 'RO1234567890', SE: 'SE123456789001',
    SI: 'SI12345678', SK: 'SK1234567890', XI: 'XI123456789',
};

// Algemene vorm (zelfde regel als RelationRules.IsVatShape): twee letters vooraan (XI voor Noord-Ierland), verder letters/cijfers.
export function isVatShape(normalized: string): boolean {
    return /^[A-Z]{2}[A-Z0-9]{2,30}$/.test(normalized);
}

export type VatProblem = 'none' | 'shape' | 'country_pattern' | 'greece_uses_el';

// Uitkomst van de controle op een genormaliseerd nummer (null = geen nummer = geen probleem).
export function checkVat(normalized: string | null): VatProblem {
    if (normalized === null) return 'none';
    if (!isVatShape(normalized)) return 'shape';
    const country = normalized.slice(0, 2);
    if (country === 'GR') return 'greece_uses_el';
    const pattern = VAT_PATTERNS[country];
    if (pattern !== undefined) return pattern.test(normalized) ? 'none' : 'country_pattern';
    return 'none';
}

// Suggestie bij GR: hetzelfde nummer met EL (alleen als dat wel de Griekse vorm heeft).
export function greekVatSuggestion(normalized: string | null): string | null {
    if (normalized === null || !normalized.startsWith('GR')) return null;
    const candidate = `EL${normalized.slice(2)}`;
    return VAT_PATTERNS.EL.test(candidate) ? candidate : null;
}
