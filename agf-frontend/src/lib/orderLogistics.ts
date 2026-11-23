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
    return { receiveLocationId: order.receiveLocationId == null ? '' : String(order.receiveLocationId), loadingAddress: loading, deliveryAddress: order.deliveryAddress ?? '', pickUp: loading !== '' };
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
