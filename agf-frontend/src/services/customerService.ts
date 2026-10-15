const API_BASE_URL = 'http://localhost:5197/api';

export const CustomerService = {
    async getStanding(id: number): Promise<unknown> {
        const res = await fetch(`${API_BASE_URL}/customers/${id}/standing`);
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    }
};
