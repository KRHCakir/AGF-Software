// Partij (spiegel van AgfAdmin/Services/PartijRules.cs): de vergelijkingssleutel voor de dubbelcontrole, de nummering (modus, formaat,
// wanneer het volgnummer opnieuw telt) en de weergavestatus. Pure module zonder React of i18n, zodat ze met node:test te toetsen is;
// dezelfde gevallen staan in AgfAdmin.Tests/Services/PartijRulesTests.cs. Het echte nummer kent altijd de server toe.

export type PartijStatus = 'Open' | 'SoldOut';
// Inkoop (één leverancier, zoals P1) of Verkoop (precies één verkooporder; de colli komen via de reserveringen uit inkoop-partijen).
export type PartijKind = 'Purchase' | 'Sales';
export type PartijNumberMode = 'Auto' | 'Own';
export type PartijSequenceReset = 'Never' | 'Yearly' | 'Monthly';

export const PARTIJ_MODES: readonly PartijNumberMode[] = ['Auto', 'Own'];
export const PARTIJ_RESETS: readonly PartijSequenceReset[] = ['Never', 'Yearly', 'Monthly'];
export const DEFAULT_PARTIJ_FORMAT = '{JJ}{MM}{volgnr}';
export const DEFAULT_SEQUENCE_DIGITS = 3;
export const MIN_SEQUENCE_DIGITS = 1;
export const MAX_SEQUENCE_DIGITS = 9;
export const PARTIJ_PLACEHOLDERS = ['{JJ}', '{JJJJ}', '{MM}', '{volgnr}'] as const;
export const MAX_PARTIJ_NUMBER_LENGTH = 100;
export const MAX_PARTIJ_FORMAT_LENGTH = 40;
export const MAX_SUPPLIER_LOT_REFERENCE_LENGTH = 100;
export const MAX_PARTIJ_REASON_LENGTH = 500;
const PLACEHOLDER = /\{[^{}]*\}/g;

// Hoofdletters, zonder spaties en streepjes: 2610251, "2610 251" en "2610-251" zijn één partij. Null als er niets overblijft.
export function partijNumberKey(number: string | null | undefined): string | null {
    if (number === null || number === undefined) return null;
    const key = number.replace(/[\s-]/g, '').toUpperCase();
    return key === '' ? null : key;
}

export type PartijFormatError = 'required' | 'too_long' | 'unknown_placeholder' | 'stray_brace' | 'sequence_once';

// Null als het formaat geldig is: precies één {volgnr}, alleen bekende plaatshouders, geen losse accolades. Vaste tekst mag (5{JJ}{MM}{volgnr}).
export function partijFormatError(format: string): PartijFormatError | null {
    if (format.trim() === '') return 'required';
    if (format.trim().length > MAX_PARTIJ_FORMAT_LENGTH) return 'too_long';
    const found = format.match(PLACEHOLDER) ?? [];
    if (found.some((placeholder) => !(PARTIJ_PLACEHOLDERS as readonly string[]).includes(placeholder))) return 'unknown_placeholder';
    const rest = format.replace(PLACEHOLDER, '');
    if (rest.includes('{') || rest.includes('}')) return 'stray_brace';
    return found.filter((placeholder) => placeholder === '{volgnr}').length === 1 ? null : 'sequence_once';
}

// {JJ}{MM}{volgnr} op 27-10-2026 met volgnummer 251 = 2610251, 5{JJ}{MM}{volgnr} op 05-11-2026 met 2 = 52611002; date is een
// kalenderdatum (jjjj-mm-dd), digits het aantal cijfers van het volgnummer (een groter volgnummer groeit gewoon door).
export function formatPartijNumber(format: string, date: string, sequence: number, digits: number = DEFAULT_SEQUENCE_DIGITS): string {
    const [year, month] = date.split('-');
    return format.trim().replace(PLACEHOLDER, (placeholder) => {
        if (placeholder === '{JJ}') return year.slice(-2);
        if (placeholder === '{JJJJ}') return year;
        if (placeholder === '{MM}') return month;
        if (placeholder === '{volgnr}') return String(sequence).padStart(Math.min(MAX_SEQUENCE_DIGITS, Math.max(MIN_SEQUENCE_DIGITS, digits)), '0');
        return placeholder;
    });
}

export function isSequenceDigits(value: number): boolean {
    return Number.isInteger(value) && value >= MIN_SEQUENCE_DIGITS && value <= MAX_SEQUENCE_DIGITS;
}

// Sleutel van de i18n-tekst per status (Open, Uitverkocht).
export const PARTIJ_STATUS_LABEL_KEYS: Record<PartijStatus, string> = { Open: 'partij_status_open', SoldOut: 'partij_status_sold_out' };
