// Kosten verdelen over partijen (spiegel van AgfAdmin/Services/PartijCostService en PartijCostRules). Pure module zonder React of i18n,
// zodat ze met node:test te toetsen is. Rekenen in centen (gehele getallen): nooit een afrondingsfout in de som.
import { parseLocaleDecimal, type NumberLocale } from './decimal.ts';

export type DistributeMode = 'equal' | 'kg' | 'colli';

// Een partij in de verdeling, met de basis voor "verdeel naar kg / colli": ontvangen (inkoop) of besteld (verkoop).
export interface CostTarget {
    partijId: number;
    number: string;
    kind: 'Purchase' | 'Sales';
    party: string | null;
    products: string | null;
    quantity: number;
    unit: string | null;
    netKg: number;
}

export interface Distribution {
    // Bedrag per partij in euro (twee decimalen), in de volgorde van de partijen; null = doet niet mee (geen kg of colli).
    amounts: (number | null)[];
    // Partijen zonder basis voor deze verdeling (zij doen niet mee; dat staat bij de partij).
    skipped: number[];
}

const toCents = (euros: number) => Math.round(euros * 100);

// Verdeel het bedrag (euro) over de partijen: gelijk, naar netto kg of naar colli. Afronden op centen naar beneden; de resterende centen
// gaan naar de grootste partij (bij gelijke grootte de eerste), zodat de som precies het bedrag is. Een partij zonder kg of colli doet
// niet mee; doet niemand mee, dan krijgt niemand iets.
export function distribute(totalEuros: number, targets: readonly CostTarget[], mode: DistributeMode): Distribution {
    const weights = targets.map((target) => (mode === 'equal' ? 1 : mode === 'kg' ? target.netKg : target.quantity));
    const skipped = targets.filter((_, index) => !(weights[index] > 0)).map((target) => target.partijId);
    const sum = weights.reduce((total, weight) => total + (weight > 0 ? weight : 0), 0);
    if (sum <= 0) return { amounts: targets.map(() => null), skipped: targets.map((target) => target.partijId) };
    const total = toCents(totalEuros);
    const cents = weights.map((weight) => (weight > 0 ? Math.floor((total * weight) / sum) : null));
    let rest = total - cents.reduce<number>((acc, value) => acc + (value ?? 0), 0);
    if (rest > 0) {
        let largest = -1;
        weights.forEach((weight, index) => { if (weight > 0 && (largest === -1 || weight > weights[largest])) largest = index; });
        cents[largest] = (cents[largest] ?? 0) + rest;
        rest = 0;
    }
    return { amounts: cents.map((value) => (value === null ? null : value / 100)), skipped };
}

export type AllocationProblem = 'amount' | 'over';

export interface AllocationCheck {
    ok: boolean;
    // Per partij een fout in het bedrag (leeg, nul, meer dan 2 decimalen, ongeldig).
    amountErrors: (string | null)[];
    allocated: number;
    // Rest: algemene kosten (alleen een waarschuwing). Negatief = meer verdeeld dan de regel (harde fout).
    general: number;
    over: boolean;
    amounts: number[];
}

// Controle vóór opslaan: elk bedrag positief met hoogstens twee decimalen, en samen niet meer dan het regelbedrag excl. btw.
export function checkAllocation(lineTotal: number, texts: readonly string[], locale?: NumberLocale): AllocationCheck {
    const amountErrors: (string | null)[] = [];
    const amounts: number[] = [];
    for (const text of texts) {
        const parsed = parseLocaleDecimal(text, locale, { maxDecimals: 2, maxDigits: 16 });
        amountErrors.push(parsed.ok ? null : parsed.error);
        amounts.push(parsed.ok ? parsed.value : 0);
    }
    const allocatedCents = amounts.reduce((total, amount) => total + toCents(amount), 0);
    const generalCents = toCents(lineTotal) - allocatedCents;
    const over = generalCents < 0;
    return { ok: !over && amountErrors.every((error) => error === null), amountErrors, allocated: allocatedCents / 100, general: generalCents / 100, over, amounts };
}
