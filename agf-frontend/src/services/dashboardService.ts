import { Shipment } from './inventoryService';
import { getApiBaseUrl } from './apiConfig';

export interface DashboardRecentOrder {
    id: number;
    orderNumber: string;
    customerName: string;
    productName: string;
    amount: number;
    status: string;
}

export interface DashboardData {
    kpis: {
        totalSalesToday: number;
        unpaidInvoices: number;
        shipmentsArriving: number;
        margin: string;
    };
    recentOrders: DashboardRecentOrder[];
    shipments: Shipment[];
}

export const DashboardService = {
    async getDashboardData(): Promise<DashboardData> {
        const res = await fetch(`${getApiBaseUrl()}/dashboard`);
        if (!res.ok) throw new Error('Failed to fetch dashboard data');
        return res.json();
    }
};
