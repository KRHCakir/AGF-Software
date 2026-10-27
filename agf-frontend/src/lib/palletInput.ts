import { parseLocaleDecimal, parsePositiveDecimal } from './decimal.ts';

// Invoer per pallet op aanvoer- en orderregels (spiegel van AgfAdmin/Services/PackagingRules.cs).
// "9 pallets × 120 dozen = 1.080 dozen": de totale hoeveelheid in verpakkingseenheden blijft de bron voor alle controles;
// het aantal pallets en de verpakkingen per pallet worden alleen bewaard voor een herkenbare weergave.
// Nieuwe invoer: hele pallets en een heel aantal per pallet, plus optioneel losse verpakkingen (3 × 120 + 15 = 375).
// Nooit een universele conversie, geen fysieke palletrecords en nooit afronden naar hele pallets.

export type QuantityInputMode = 'packages' | 'pallets';

export interface PalletInputDraft {
    mode: QuantityInputMode;
    quantity: string;
    palletCount: string;
    packagesPerPallet: string;
    loosePackages?: string;
}

export interface PalletInputValue {
    quantity: number;
    palletCount: number | null;
    packagesPerPallet: number | null;
    loosePackages: number | null;
}

export type PalletInputError = 'palletCount' | 'packagesPerPallet' | 'loosePackages';

// Zelfde afronding als de server: 3 decimalen, half omhoog.
export function palletTotal(palletCount: number, packagesPerPallet: number, loosePackages = 0): number {
    const scaled = (palletCount * packagesPerPallet + loosePackages) * 1000;
    return Math.round(scaled + Number.EPSILON * Math.abs(scaled)) / 1000;
}

// Heel aantal (pallets en per pallet > 0, losse verpakkingen >= 0); een breuk zoals 3,5 pallets geeft 'not_whole'.
function wholeCount(input: string, allowZero = false): { ok: true; value: number } | { ok: false; error: string } {
    const parsed = parseLocaleDecimal(input, undefined, { allowZero });
    return parsed.ok && !Number.isInteger(parsed.value) ? { ok: false, error: 'not_whole' } : parsed;
}

// Leest de palletinvoer; in de stand "verpakkingen" telt alleen de hoeveelheid en blijven de palletvelden null.
export function resolvePalletInput(draft: PalletInputDraft):
    | { ok: true; value: PalletInputValue }
    | { ok: false; errors: { field: PalletInputError | 'quantity'; code: string }[] } {
    if (draft.mode === 'packages') {
        const quantity = parsePositiveDecimal(draft.quantity);
        return quantity.ok
            ? { ok: true, value: { quantity: quantity.value, palletCount: null, packagesPerPallet: null, loosePackages: null } }
            : { ok: false, errors: [{ field: 'quantity', code: quantity.error }] };
    }
    const errors: { field: PalletInputError | 'quantity'; code: string }[] = [];
    const count = wholeCount(draft.palletCount);
    const perPallet = wholeCount(draft.packagesPerPallet);
    // Losse verpakkingen zijn optioneel: leeg gaat als null mee.
    const loose = (draft.loosePackages ?? '').trim() === '' ? { ok: true as const, value: null } : wholeCount(draft.loosePackages!, true);
    if (!count.ok) errors.push({ field: 'palletCount', code: count.error });
    if (!perPallet.ok) errors.push({ field: 'packagesPerPallet', code: perPallet.error });
    if (!loose.ok) errors.push({ field: 'loosePackages', code: loose.error });
    if (!count.ok || !perPallet.ok || !loose.ok) return { ok: false, errors };
    return { ok: true, value: { quantity: palletTotal(count.value, perPallet.value, loose.value ?? 0), palletCount: count.value, packagesPerPallet: perPallet.value, loosePackages: loose.value } };
}

// Herkenbare weergave van een opgeslagen regel: null bij gewone invoer.
export function palletBreakdown(line: { palletCount: number | null; packagesPerPallet: number | null }): { palletCount: number; packagesPerPallet: number } | null {
    return line.palletCount !== null && line.packagesPerPallet !== null ? { palletCount: line.palletCount, packagesPerPallet: line.packagesPerPallet } : null;
}
