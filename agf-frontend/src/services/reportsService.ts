const API_BASE_URL = 'http://localhost:5197/api';

export const ReportsService = {
    async traceShipment(shipmentId: number): Promise<any> {
        const res = await fetch(`${API_BASE_URL}/reports/trace/shipment/${shipmentId}`);
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    },

    async traceInvoice(invoiceId: number): Promise<any> {
        const res = await fetch(`${API_BASE_URL}/reports/trace/invoice/${invoiceId}`);
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    }
};
