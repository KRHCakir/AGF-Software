import type { WeightSource } from '../types/salesOrder';
import { parsePositiveDecimal, type DecimalError } from './decimal.ts';
import { resolvePalletInput, type PalletInputDraft } from './palletInput.ts';
import { times, type PackageKgDefaults } from './quantityKg.ts';

// Netto en bruto kg op een order- of aanvoerregel, spiegel van LineWeightRules in de backend; de server blijft leidend.
// Per veld: met een standaard kg per verpakking van het artikel volgt het veld live aantal × standaard ("berekend"), tot de
// gebruiker zelf typt ("ingevuld"); dan volgt het het aantal niet meer, tot ↺ het terugzet. Een leeg gemaakt ingevuld veld valt
// terug op de berekening. Zonder standaard is het veld gewoon leeg en optioneel. Het artikel verandert hierdoor nooit.
// Bron per regel: geen kg -> null; minstens één ingevuld veld -> Entered; anders Calculated.
// Pure module zonder React of i18n (node:test); imports met .ts-extensie.

export interface LineWeightDraft {
    netKg?: string;
    grossKg?: string;
    // true = door de gebruiker getypt; ontbreekt of false = volgt de berekening (als die er is).
    netKgEntered?: boolean;
    grossKgEntered?: boolean;
}

export type KgField = 'net' | 'gross';

export interface CalculatedKg {
    net: number | null;
    gross: number | null;
}

export interface LineWeight {
    netWeightKg: number | null;
    grossWeightKg: number | null;
    weightSource: WeightSource | null;
}

export type LineWeightError = { field: KgField; code: DecimalError | 'gross_below_net' };

// Aantal × standaard per veld, alleen met een geldig aantal en een positieve standaard; anders null (geen berekening).
export function calculatedKg(quantity: number | null, defaults: PackageKgDefaults | undefined): CalculatedKg {
    const per = (kg: number | null | undefined) => (quantity !== null && quantity > 0 && kg != null && kg > 0 ? times(quantity, kg) : null);
    return { net: per(defaults?.defaultNetKgPerPackage), gross: per(defaults?.defaultGrossKgPerPackage) };
}

// Wordt het veld als ingevuld behandeld? Alleen met getypte tekst; leeg maken betekent terug naar de berekening.
export function isEntered(draft: LineWeightDraft, field: KgField): boolean {
    const text = field === 'net' ? draft.netKg : draft.grossKg;
    const entered = field === 'net' ? draft.netKgEntered : draft.grossKgEntered;
    return entered === true && (text ?? '').trim() !== '';
}

export function resolveLineWeight(draft: LineWeightDraft, quantity: number | null, defaults: PackageKgDefaults | undefined): { ok: true; value: LineWeight } | { ok: false; errors: LineWeightError[] } {
    const calculated = calculatedKg(quantity, defaults);
    const errors: LineWeightError[] = [];
    const read = (field: KgField): number | null => {
        if (!isEntered(draft, field)) return calculated[field];
        const parsed = parsePositiveDecimal((field === 'net' ? draft.netKg : draft.grossKg) ?? '');
        if (!parsed.ok) {
            errors.push({ field, code: parsed.error });
            return null;
        }
        return parsed.value;
    };
    const net = read('net');
    const gross = read('gross');
    if (errors.length === 0 && net !== null && gross !== null && gross < net) errors.push({ field: 'gross', code: 'gross_below_net' });
    if (errors.length > 0) return { ok: false, errors };
    const source: WeightSource | null = net === null && gross === null ? null
        : isEntered(draft, 'net') || isEntered(draft, 'gross') ? 'Entered' : 'Calculated';
    return { ok: true, value: { netWeightKg: net, grossWeightKg: gross, weightSource: source } };
}

// Alleen ingevulde waarden mee in de payload: een regel zonder kg blijft dezelfde opdracht als vóór deze velden.
export function weightPayload<N extends string, G extends string>(weight: LineWeight, netName: N, grossName: G): Partial<Record<N | G, number>> & { weightSource?: WeightSource } {
    return {
        ...(weight.netWeightKg !== null ? { [netName]: weight.netWeightKg } : {}),
        ...(weight.grossWeightKg !== null ? { [grossName]: weight.grossWeightKg } : {}),
        ...(weight.weightSource !== null ? { weightSource: weight.weightSource } : {}),
    } as Partial<Record<N | G, number>> & { weightSource?: WeightSource };
}

// Controle bij zelf ingevulde netto kg: "= 10,0 kg per colli" (alleen weergave; het artikel wordt nooit aangepast).
export function kgPerPackage(netKg: number | null, quantity: number | null): number | null {
    return netKg !== null && quantity !== null && quantity > 0 ? netKg / quantity : null;
}

// Totaal onder de regels: aantallen per eenheid (eenheden worden nooit opgeteld of omgerekend) en de kg van de regels die kg
// hebben; het aantal regels zonder netto of bruto kg wordt apart genoemd, zodat een totaal nooit stilzwijgend onvolledig is.
export interface WeightTotals {
    quantities: { unit: string; quantity: number }[];
    netKg: number;
    grossKg: number;
    linesWithoutNet: number;
    linesWithoutGross: number;
}

export function lineWeightTotals(lines: { quantity: number | null; unit: string; netWeightKg: number | null; grossWeightKg: number | null }[]): WeightTotals {
    const byUnit = new Map<string, number>();
    let netKg = 0;
    let grossKg = 0;
    let linesWithoutNet = 0;
    let linesWithoutGross = 0;
    for (const line of lines) {
        if (line.quantity !== null && line.unit !== '') byUnit.set(line.unit, Math.round(((byUnit.get(line.unit) ?? 0) + line.quantity) * 1000) / 1000);
        if (line.netWeightKg === null) linesWithoutNet += 1;
        else netKg += line.netWeightKg;
        if (line.grossWeightKg === null) linesWithoutGross += 1;
        else grossKg += line.grossWeightKg;
    }
    return {
        quantities: Array.from(byUnit, ([unit, quantity]) => ({ unit, quantity })),
        netKg: Math.round(netKg * 1000) / 1000,
        grossKg: Math.round(grossKg * 1000) / 1000,
        linesWithoutNet,
        linesWithoutGross,
    };
}

// Per regel van een formulier: het aantal (ook bij palletinvoer), de standaard van het artikel, de berekende kg en de kg die
// zou worden verstuurd (null bij een invoerfout); plus het totaal onder de regels.
export interface LineKgRow {
    quantity: number | null;
    defaults: PackageKgDefaults | undefined;
    calculated: CalculatedKg;
    weight: LineWeight | null;
}

export function lineKgRows(lines: (LineWeightDraft & PalletInputDraft & { productVariantId: string; unit: string })[], defaultsFor: (variantId: number) => PackageKgDefaults | undefined): { rows: LineKgRow[]; totals: WeightTotals } {
    const rows = lines.map((line) => {
        const amounts = resolvePalletInput(line);
        const quantity = amounts.ok ? amounts.value.quantity : null;
        const defaults = defaultsFor(Number(line.productVariantId));
        const weight = resolveLineWeight(line, quantity, defaults);
        return { quantity, defaults, calculated: calculatedKg(quantity, defaults), weight: weight.ok ? weight.value : null };
    });
    const totals = lineWeightTotals(lines.map((line, index) => ({
        quantity: rows[index].quantity, unit: line.unit, netWeightKg: rows[index].weight?.netWeightKg ?? null, grossWeightKg: rows[index].weight?.grossWeightKg ?? null,
    })));
    return { rows, totals };
}
