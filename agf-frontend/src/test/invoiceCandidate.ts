import type { InvoiceCandidate } from '../types/purchaseOrder';

// Een order om een factuur aan te koppelen (stap 4b): Avocado Hass, 264 colli × 13,50 = 3.564,00, niets ontvangen of gefactureerd.
export const candidate = (over: Partial<InvoiceCandidate> = {}): InvoiceCandidate => ({
    id: 56, orderNumber: 'IO-2026-0056', supplierReference: 'NP-1', orderedOn: '2026-11-10', expectedAt: null, partijNumber: 'P-26-0012', documentStatus: 'Open', route: 'Warehouse',
    priceAdjustable: true, invoiceNumbers: [],
    lines: [{ id: 81, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', unit: 'colli', expectedQuantity: 264, receivedQuantity: 0, invoicedQuantity: 0, openQuantity: 264, unitPrice: 13.5, partijNumber: 'P-26-0012' }],
    ...over,
});
