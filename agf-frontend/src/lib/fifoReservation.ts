import { parsePositiveDecimal } from './decimal.ts';
import type { RuleCode, RuleError } from './salesOrderRules.ts';
import { validateReservationInput } from './salesOrderRules.ts';
import { calculatedReservationKg } from './weight.ts';

// Het aanpasbare FIFO-voorstel ("Reserveren (oudste partij eerst)"), puur en los van React. Elke rij is één inhoudsregel; een lege rij
// doet niet mee. Per rij gelden dezelfde regels als bij één reservering (niet boven de inhoud, geen gestrande rest; berekend gewicht volgt
// het aantal), en samen nooit meer dan de regel nog nodig heeft. De server controleert alles opnieuw, in één transactie.

export interface FifoCandidate {
    palletContentLineId: number;
    availableQuantity: number;
    availableNetWeightKg: number;
    weightMethod: string;
    netKgPerPackage: number | null;
}

export interface FifoRowInput {
    quantity: string;
    netKg: string;
}

export interface FifoItem {
    palletContentLineId: number;
    quantity: number;
    netWeightKg: number;
}

export type FifoResult = { ok: true; items: FifoItem[]; total: number } | { ok: false; errors: RuleError[] };

// Berekend gewicht: de kg volgt exact het aantal (de hele rest neemt de resterende kg); anders null (gewogen: zelf invullen of voorstel).
export function calculatedKgFor(candidate: FifoCandidate, quantityText: string): number | null {
    if (candidate.weightMethod !== 'Calculated' || candidate.netKgPerPackage === null) return null;
    const quantity = parsePositiveDecimal(quantityText);
    return quantity.ok ? calculatedReservationKg(quantity.value, candidate.netKgPerPackage, candidate.availableQuantity, candidate.availableNetWeightKg) : null;
}

export function sumQuantities(rows: Record<number, FifoRowInput>): number {
    let total = 0;
    for (const row of Object.values(rows)) {
        const quantity = parsePositiveDecimal(row.quantity);
        if (quantity.ok) total += quantity.value;
    }
    return Math.round(total * 1000) / 1000;
}

export function validateFifo(candidates: FifoCandidate[], rows: Record<number, FifoRowInput>, needed: number): FifoResult {
    const errors: RuleError[] = [];
    const items: FifoItem[] = [];
    let total = 0;
    for (const candidate of candidates) {
        const row = rows[candidate.palletContentLineId];
        if (!row || row.quantity.trim() === '') continue;
        const calculated = calculatedKgFor(candidate, row.quantity);
        const netKg = calculated === null ? row.netKg : String(calculated);
        const result = validateReservationInput({ quantity: row.quantity, netKg }, candidate, Number.MAX_SAFE_INTEGER);
        if (!result.ok) {
            for (const error of result.errors) errors.push({ field: `items[${candidate.palletContentLineId}].${error.field}`, code: error.code });
            continue;
        }
        items.push({ palletContentLineId: candidate.palletContentLineId, quantity: result.value.quantity, netWeightKg: result.value.netWeightKg });
        total += result.value.quantity;
    }
    total = Math.round(total * 1000) / 1000;
    if (errors.length === 0 && items.length === 0) errors.push({ field: 'items', code: 'required' as RuleCode });
    if (total > needed) errors.push({ field: 'items', code: 'exceeds_demand' });
    return errors.length > 0 ? { ok: false, errors } : { ok: true, items, total };
}
