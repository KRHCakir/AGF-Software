import type { PurchaseOrderDetail } from '../types/purchaseOrder';

// De volgende stap op een inkooporder (november 2026). Pure module: met node:test te toetsen.

type Order = Pick<PurchaseOrderDetail, 'id' | 'route' | 'status' | 'storedStatus' | 'lines' | 'billingStatus'>;

const cancelled = (order: Order) => order.status === 'Cancelled' || order.storedStatus === 'Cancelled';

// Ontvangen kan zolang van een order voor het eigen magazijn nog iets openstaat.
export const canReceive = (order: Order): boolean =>
    order.route === 'Warehouse' && !cancelled(order) && order.lines.some((line) => line.receivedQuantity < line.expectedQuantity);

// Factuur invoeren bovenaan: een gewone knop zolang er nog iets te ontvangen is (Ontvangen is dan de hoofdknop), de hoofdknop zodra alles
// ontvangen is, en weg als alles gefactureerd is of de order vervallen is.
export function invoiceButton(order: Order): 'primary' | 'secondary' | null {
    if (cancelled(order) || order.billingStatus === 'Full') return null;
    return canReceive(order) ? 'secondary' : 'primary';
}

// Factuur invoeren voor deze order: de inkoopfactuur met de leverancier en de regels van de bon voorgevuld.
export const invoiceEntryHref = (orderId: number): string => `/purchase-invoices/new?orderId=${orderId}`;
