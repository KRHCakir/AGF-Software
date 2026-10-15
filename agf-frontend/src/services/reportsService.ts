const API_BASE_URL = 'http://localhost:5197/api';

export interface ShipmentTrace {
    shipmentId: number;
    totalBatches: number;
    totalWastedUnits: number;
    wastedReasons: { reason: string; count: number }[];
    customers: {
        customerName: string;
        orderId: number;
        invoiceNumber: string;
        quantityAllocated: number;
        dateSold: string;
    }[];
}

export interface InvoiceTrace {
    invoiceId: number;
    invoiceNumber: string;
    sources: {
        productName: string;
        batchQuantity: number;
        inventoryBatchId: number;
        shipmentId: number | null;
        containerCode: string;
        origin: string;
    }[];
}

export const ReportsService = {
    async traceShipment(shipmentId: number): Promise<ShipmentTrace> {
        const res = await fetch(`${API_BASE_URL}/reports/trace/shipment/${shipmentId}`);
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    },

    async traceInvoice(invoiceId: number): Promise<InvoiceTrace> {
        const res = await fetch(`${API_BASE_URL}/reports/trace/invoice/${invoiceId}`);
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    }
};
