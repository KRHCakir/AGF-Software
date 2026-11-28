import type { StockLocation } from '../types/stockLocation';

// Feedback 22 november: ontvangstlocatie (leeg = de eigen loods), laadadres (leeg als de leverancier levert; het adres van de leverancier
// als we ophalen) en afleveradres (leeg = de eigen loods; de klant bij directe handel) van een inkooporder. Pure module: met node:test te toetsen.

export interface OrderLogisticsDraft {
    receiveLocationId: string;
    loadingAddress: string;
    deliveryAddress: string;
    pickUp: boolean;
}

export interface OrderLogisticsPayload {
    receiveLocationId: number | null;
    loadingAddress: string | null;
    deliveryAddress: string | null;
}

export const emptyLogistics = (): OrderLogisticsDraft => ({ receiveLocationId: '', loadingAddress: '', deliveryAddress: '', pickUp: false });

export function logisticsOf(order: { receiveLocationId?: number | null; loadingAddress?: string | null; deliveryAddress?: string | null }): OrderLogisticsDraft {
    const loading = order.loadingAddress ?? '';
    return { receiveLocationId: order.receiveLocationId == null ? '' : String(order.receiveLocationId), loadingAddress: loading, deliveryAddress: order.deliveryAddress ?? '', pickUp: loading.trim() !== '' };
}

export function logisticsPayload(draft: OrderLogisticsDraft): OrderLogisticsPayload {
    const text = (value: string) => (value.trim() === '' ? null : value.trim());
    return { receiveLocationId: draft.receiveLocationId === '' ? null : Number(draft.receiveLocationId), loadingAddress: text(draft.loadingAddress), deliveryAddress: text(draft.deliveryAddress) };
}

// De keuzelijst: de actieve locaties, plus de opgeslagen locatie van de order als die intussen inactief is (die mag blijven).
export function locationChoices(locations: readonly StockLocation[], current: string): StockLocation[] {
    return locations.filter((location) => location.isActive || String(location.id) === current);
}

export const isInTransit = (locations: readonly StockLocation[], id: string): boolean => locations.some((location) => String(location.id) === id && location.kind === 'InTransit');

// November 2026: meer dan één eigen loods (de ingebouwde plus minstens één actieve locatie van de soort Eigen loods). Dan staat de keuzelijst
// altijd open, met de standaard ontvangstlocatie voorgekozen; anders is Eigen loods de standaard als tekst met Afwijkend?.
export const hasSeveralOwnWarehouses = (locations: readonly StockLocation[]): boolean => locations.some((location) => location.isActive && location.kind === 'OwnWarehouse');

// De ontvangstlocatie van een nieuwe order: de standaard uit de instellingen, maar alleen bij meer dan één eigen loods en als die nog actief is.
export function defaultReceiveLocation(locations: readonly StockLocation[], defaultId: number | null): string {
    if (defaultId === null || !hasSeveralOwnWarehouses(locations)) return '';
    return locations.some((location) => location.id === defaultId && location.isActive && location.kind === 'OwnWarehouse') ? String(defaultId) : '';
}

// November 2026: de klant om voor te stellen bij Direct naar klant: alleen als de verkooporders die van deze inkooporder verkopen precies
// één klant hebben (anders geen voorstel).
export function suggestedDeliveryCustomer(links: readonly { customerId?: number | null }[] | undefined): number | null {
    const ids = new Set((links ?? []).map((link) => link.customerId).filter((id): id is number => typeof id === 'number'));
    return ids.size === 1 ? [...ids][0] : null;
}

// De naam van de gekozen locatie voor de tekstweergave (leeg = Eigen loods; een onbekende id als nummer).
export function receiveLocationName(locations: readonly StockLocation[], id: string, own: string): string {
    if (id === '') return own;
    return locations.find((location) => String(location.id) === id)?.name ?? `#${id}`;
}
