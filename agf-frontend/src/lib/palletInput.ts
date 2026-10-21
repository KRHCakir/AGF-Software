import { parsePositiveDecimal } from './decimal.ts';

// Invoer per pallet op aanvoer- en orderregels (spiegel van AgfAdmin/Services/PackagingRules.cs).
// "9 pallets × 120 dozen = 1.080 dozen": de totale hoeveelheid in verpakkingseenheden blijft de bron voor alle controles;
// het aantal pallets en de verpakkingen per pallet worden alleen bewaard voor een herkenbare weergave.
// Nooit een universele conversie, geen fysieke palletrecords en nooit afronden naar hele pallets.

export type QuantityInputMode = 'packages' | 'pallets';

export interface PalletInputDraft {
    mode: QuantityInputMode;
    quantity: string;
    palletCount: string;
    packagesPerPallet: string;
}

export interface PalletInputValue {
    quantity: number;
    palletCount: number | null;
    packagesPerPallet: number | null;
}

export type PalletInputError = 'palletCount' | 'packagesPerPallet';

// Zelfde afronding als de server: 3 decimalen, half omhoog.
export function palletTotal(palletCount: number, packagesPerPallet: number): number {
    const scaled = palletCount * packagesPerPallet * 1000;
    return Math.round(scaled + Number.EPSILON * Math.abs(scaled)) / 1000;
}

// Leest de palletinvoer; in de stand "verpakkingen" telt alleen de hoeveelheid en blijven de palletvelden null.
export function resolvePalletInput(draft: PalletInputDraft):
    | { ok: true; value: PalletInputValue }
    | { ok: false; errors: { field: PalletInputError | 'quantity'; code: string }[] } {
    if (draft.mode === 'packages') {
        const quantity = parsePositiveDecimal(draft.quantity);
        return quantity.ok
            ? { ok: true, value: { quantity: quantity.value, palletCount: null, packagesPerPallet: null } }
            : { ok: false, errors: [{ field: 'quantity', code: quantity.error }] };
    }
    const errors: { field: PalletInputError | 'quantity'; code: string }[] = [];
    const count = parsePositiveDecimal(draft.palletCount);
    const perPallet = parsePositiveDecimal(draft.packagesPerPallet);
    if (!count.ok) errors.push({ field: 'palletCount', code: count.error });
    if (!perPallet.ok) errors.push({ field: 'packagesPerPallet', code: perPallet.error });
    if (!count.ok || !perPallet.ok) return { ok: false, errors };
    return { ok: true, value: { quantity: palletTotal(count.value, perPallet.value), palletCount: count.value, packagesPerPallet: perPallet.value } };
}

// Herkenbare weergave van een opgeslagen regel: null bij gewone invoer.
export function palletBreakdown(line: { palletCount: number | null; packagesPerPallet: number | null }): { palletCount: number; packagesPerPallet: number } | null {
    return line.palletCount !== null && line.packagesPerPallet !== null ? { palletCount: line.palletCount, packagesPerPallet: line.packagesPerPallet } : null;
}
