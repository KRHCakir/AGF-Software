import type { SalesOrder } from '../types/salesOrder';

// De volgorde op een verkooporder: 1. reserveren (of klantgebonden aanvoer toewijzen), 2. verzenden, 3. aflevering bevestigen. Welke stap
// nu aan de beurt is, volgt uit de regels zoals de server ze geeft (open, gereserveerd, verzonden, afgeleverd); niets afgeleid daarbuiten.
// Geen imports buiten typen: met node:test te toetsen.

export type SalesStep = 'reserve' | 'dispatch' | 'confirm';
export type SalesNextStep = SalesStep | 'done' | 'cancelled';
export const SALES_STEPS: readonly SalesStep[] = ['reserve', 'dispatch', 'confirm'];

export function salesNextStep(order: Pick<SalesOrder, 'status' | 'delivered' | 'lines'>): SalesNextStep {
    if (order.status === 'Cancelled') return 'cancelled';
    if (order.lines.some((line) => line.openQuantity > 0)) return 'reserve';
    if (order.lines.some((line) => line.reservedQuantity > 0 || line.allocatedQuantity > 0)) return 'dispatch';
    if (!order.delivered) return 'confirm';
    return 'done';
}
