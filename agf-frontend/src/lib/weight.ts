// Gewichtsmethode van een inhoudsregel of productvariant (spiegel van AgfAdmin/Services/WeightRules.cs).
// - Weighed: het totale netto gewicht is werkelijk gewogen en expliciet ingevoerd.
// - Calculated: totaal = aantal verpakkingen × netto kg per verpakking (etiket). Nooit als gewogen gepresenteerd.
// Pure module zonder React of i18n, zodat ze met node:test te toetsen is.

export type WeightMethod = 'Weighed' | 'Calculated';
export const WEIGHT_METHODS: readonly WeightMethod[] = ['Weighed', 'Calculated'];

export function isWeightMethod(value: unknown): value is WeightMethod {
    return value === 'Weighed' || value === 'Calculated';
}

// Dezelfde afronding als de server (3 decimalen, half omhoog), zodat client en server hetzelfde totaal berekenen.
export function calculatedTotalKg(quantity: number, netKgPerPackage: number): number {
    const scaled = quantity * netKgPerPackage * 1000;
    return Math.round(scaled + Number.EPSILON * Math.abs(scaled)) / 1000;
}

export const WEIGHT_METHOD_LABEL_KEYS: Readonly<Record<WeightMethod, string>> = {
    Weighed: 'weight_weighed',
    Calculated: 'weight_calculated',
};

export type WeightBasis = 'weighed' | 'calculated' | 'mixed';

export function isWeightBasis(value: unknown): value is WeightBasis {
    return value === 'weighed' || value === 'calculated' || value === 'mixed';
}

export const WEIGHT_BASIS_LABEL_KEYS: Readonly<Record<WeightBasis, string>> = {
    weighed: 'weight_basis_weighed',
    calculated: 'weight_basis_calculated',
    mixed: 'weight_basis_mixed',
};
