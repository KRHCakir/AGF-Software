import type { PackageUnit } from '../types/availability';
import type { SalesBalance, SalesLineCoverage, SupplySource } from '../types/salesSupply';

// Verkopen vóór aankomst: pure regels zonder React of i18n (spiegel van SalesSupplyRules in de backend; de server blijft leidend).
// Vrij te verkopen = Voorraad + Verwacht - Verkocht en mag negatief zijn (tekort: waarschuwing, geen blokkade). Een regel "Uit voorraad"
// past altijd in de vrije voorraad; anders stelt het systeem voor te splitsen (deel uit voorraad, rest uit verwachte aanvoer).
// Let op: imports met .ts-extensie zijn niet nodig; dit bestand importeert alleen types.

export interface SourceSplit {
    stock: number;
    expected: number;
}

// Zelfde gevallen als SalesSupplyRules.Propose: past het in de vrije voorraad, dan alles uit voorraad; anders het vrije deel uit
// voorraad en de rest uit verwachte aanvoer. Nooit een negatief deel.
export function proposeSplit(quantity: number, freeStock: number): SourceSplit {
    if (!(quantity > 0)) return { stock: 0, expected: 0 };
    const stock = Math.min(quantity, Math.max(0, freeStock));
    return { stock, expected: round3(quantity - stock) };
}

// De bron die bij een ingevuld aantal past zonder te splitsen, of null als splitsen nodig is.
export function singleSource(quantity: number, freeStock: number): SupplySource | null {
    const split = proposeSplit(quantity, freeStock);
    if (split.expected === 0) return 'Stock';
    return split.stock === 0 ? 'Expected' : null;
}

export type FreeState = 'shortage' | 'zero' | 'free';

export function freeState(free: number): FreeState {
    return free < 0 ? 'shortage' : free === 0 ? 'zero' : 'free';
}

// Knop "Verkopen" vanuit de beschikbaarheid: standaard het vrije aantal, minimaal 1.
export function sellQuantity(balance: Pick<SalesBalance, 'free'> | null | undefined): number {
    const free = balance?.free ?? 0;
    return free >= 1 ? round3(free) : 1;
}

export function sellHref(productVariantId: number, unit: PackageUnit, quantity: number): string {
    const params = new URLSearchParams({ new: '1', variantId: String(productVariantId), unit, quantity: String(quantity) });
    return `/sales-orders?${params.toString()}`;
}

export interface SellPrefill {
    productVariantId: number;
    unit: PackageUnit;
    quantity: number;
}

const UNITS: readonly string[] = ['box', 'colli', 'tray', 'piece', 'dozen'];

// De parameters van de voorinvulling; de werkplek haalt ze weg zodra een order gekozen of het formulier gesloten wordt.
export const SELL_PARAMS: readonly string[] = ['new', 'variantId', 'unit', 'quantity'];

// Alleen een volledige, geldige voorinvulling; anders null (dan een leeg formulier).
export function parseSellPrefill(params: { get(name: string): string | null }): SellPrefill | null {
    if (params.get('new') !== '1') return null;
    const variant = params.get('variantId');
    const unit = params.get('unit');
    const quantityText = params.get('quantity');
    if (variant === null || !/^[1-9][0-9]{0,9}$/.test(variant) || Number(variant) > 2147483647) return null;
    if (unit === null || !UNITS.includes(unit)) return null;
    const quantity = quantityText === null ? NaN : Number(quantityText);
    if (!Number.isFinite(quantity) || quantity <= 0 || Math.round(quantity * 1000) !== quantity * 1000) return null;
    return { productVariantId: Number(variant), unit: unit as PackageUnit, quantity };
}

export type CoveragePart =
    | { kind: 'stock'; quantity: number }
    | { kind: 'inspection'; quantity: number; orderNumber: string | null; supplyCommitmentId: number }
    | { kind: 'expected'; quantity: number; orderNumber: string | null; supplyCommitmentId: number; expectedAt: string | null }
    | { kind: 'shortage'; quantity: number };

// Dekking van het niet-gereserveerde deel van een regel als leesbare delen: nu uit voorraad, in keuring of verwacht per inkooporder,
// en het tekort. Een regel zonder open vraag heeft geen delen.
export function coverageParts(coverage: SalesLineCoverage | null | undefined): CoveragePart[] {
    if (!coverage) return [];
    const parts: CoveragePart[] = [];
    if (coverage.fromStock > 0) parts.push({ kind: 'stock', quantity: coverage.fromStock });
    for (const cover of coverage.expected) {
        if (cover.quantity <= 0) continue;
        parts.push(cover.inInspection
            ? { kind: 'inspection', quantity: cover.quantity, orderNumber: cover.supplyOrderNumber, supplyCommitmentId: cover.supplyCommitmentId }
            : { kind: 'expected', quantity: cover.quantity, orderNumber: cover.supplyOrderNumber, supplyCommitmentId: cover.supplyCommitmentId, expectedAt: cover.expectedAt });
    }
    if (coverage.shortage > 0) parts.push({ kind: 'shortage', quantity: coverage.shortage });
    return parts;
}

// Aantal pallets ter informatie ("= 3,2 pallets"); null zonder bekend aantal per pallet. Nooit afgerond naar hele pallets.
export function palletEquivalent(quantity: number, packagesPerPallet: number | null | undefined): number | null {
    if (!packagesPerPallet || packagesPerPallet <= 0 || !(quantity > 0)) return null;
    return Math.round((quantity / packagesPerPallet) * 10) / 10;
}

// "verwacht 05-11": dag en maand in de eigen tijdzone (zoals het datumveld dd-mm-jjjj); een onleesbare datum blijft de tekst.
export function dayMonth(iso: string): string {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return iso;
    return `${String(date.getDate()).padStart(2, '0')}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

function round3(value: number): number {
    return Math.round(value * 1000) / 1000;
}
