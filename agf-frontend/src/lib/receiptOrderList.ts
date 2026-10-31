import type { SupplyCommitment, SupplyCommitmentLine } from '../types/supply';
import { countryKey } from './countries.ts';

// Lijst met open orders bij "Pallet ontvangen" (30 orders per dag): zoeken op leverancier, ordernummer, leveranciersreferentie,
// partij en product (elk woord moet ergens voorkomen, in willekeurige volgorde), eerst wat vandaag of eerder verwacht werd, en
// "regel 1 van 3" bij meerdere regels van hetzelfde product in één order. De te controleren kenmerken (herkomst, maat, verpakking,
// klasse, merk, kg per colli) staan hier bewust niet in: blinde invoer.
// Geen imports buiten types en lib: met node:test te toetsen.

export interface OrderGroup {
    commitment: SupplyCommitment;
    lines: SupplyCommitmentLine[];
    // Verwacht op of vóór vandaag (Europe/Amsterdam): bovenaan.
    due: boolean;
}

// De kalenderdag van een tijdstip in Europe/Amsterdam (yyyy-MM-dd), zoals todayInAmsterdam.
const amsterdamDay = (iso: string) =>
    new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Amsterdam', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(iso));

function orderText(commitment: SupplyCommitment): string {
    return [commitment.orderNumber ?? '', String(commitment.id), commitment.vendorName ?? '', commitment.supplierReference ?? ''].join(' ');
}

function lineText(line: SupplyCommitmentLine): string {
    return [line.productName ?? '', line.productVariantName ?? '', line.lotNumber ?? ''].join(' ');
}

export function orderListGroups(commitments: readonly SupplyCommitment[], query: string, today: string): OrderGroup[] {
    const words = countryKey(query).split(' ').filter((word) => word !== '');
    const groups: OrderGroup[] = [];
    for (const commitment of commitments) {
        const order = countryKey(orderText(commitment));
        const lines = commitment.lines.filter((line) => {
            const text = `${order} ${countryKey(lineText(line))}`;
            return words.every((word) => text.includes(word));
        });
        if (lines.length === 0) continue;
        const day = commitment.expectedAt ? amsterdamDay(commitment.expectedAt) : null;
        groups.push({ commitment, lines, due: day !== null && day <= today });
    }
    const time = (group: OrderGroup) => (group.commitment.expectedAt ? new Date(group.commitment.expectedAt).getTime() : Number.POSITIVE_INFINITY);
    return groups.sort((a, b) => Number(b.due) - Number(a.due) || time(a) - time(b)
        || (a.commitment.orderNumber ?? '').localeCompare(b.commitment.orderNumber ?? '') || a.commitment.id - b.commitment.id);
}

// "Regel 1 van 3": alleen bij meer dan één regel van hetzelfde product (productsoort en ras) in één order, op volgorde van de order.
export function lineOrdinals(lines: readonly SupplyCommitmentLine[]): Map<number, { index: number; of: number }> {
    const byArticle = new Map<string, number[]>();
    for (const line of lines) {
        const key = countryKey(`${line.productName ?? ''} ${line.productVariantName ?? ''}`);
        byArticle.set(key, [...(byArticle.get(key) ?? []), line.id]);
    }
    const result = new Map<number, { index: number; of: number }>();
    for (const ids of byArticle.values()) {
        if (ids.length > 1) ids.forEach((id, index) => result.set(id, { index: index + 1, of: ids.length }));
    }
    return result;
}
