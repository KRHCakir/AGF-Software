import type { PackageUnit } from '../types/availability';

// Eén gedeelde, vertaalde weergave van verpakkingseenheden. De interne codes (box, colli, tray, piece, dozen) blijven
// de opgeslagen waarden; alleen het label verandert per taal. 'dozen' is Engels voor dozijn en wordt daarom altijd als
// "Dozijn (12 stuks)" getoond, nooit als het Nederlandse meervoud van doos. Er wordt nooit tussen eenheden omgerekend.

export const UNIT_LABEL_KEYS: Readonly<Record<PackageUnit, string>> = {
    box: 'unit_box',
    colli: 'unit_colli',
    tray: 'unit_tray',
    piece: 'unit_piece',
    dozen: 'unit_dozen',
};

export function unitLabelKey(unit: PackageUnit): string {
    return UNIT_LABEL_KEYS[unit];
}

// Voor lopende tekst; `t` is de vertaalfunctie van useTranslation.
export function unitLabel(t: (key: string) => string, unit: PackageUnit): string {
    return t(unitLabelKey(unit));
}
