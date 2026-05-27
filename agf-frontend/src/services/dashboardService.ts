export interface DashboardData {
    kpis: {
        totalSalesToday: number;
        unpaidInvoices: number;
        shipmentsArriving: number;
        margin: string;
    };
    recentOrders: any[];
    shipments: any[];
}

const API_BASE_URL = 'http://localhost:5197/api';

export const DashboardService = {
    async getDashboardData(): Promise<DashboardData> {
        const res = await fetch(`${API_BASE_URL}/dashboard`);
        if (!res.ok) throw new Error('Failed to fetch dashboard data');
        return res.json();
    }
};
