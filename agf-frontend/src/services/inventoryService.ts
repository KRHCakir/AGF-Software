import { ApiMessage } from './apiTypes';

const API_BASE_URL = 'http://localhost:5197/api';

export interface InventoryItem {
    id: number;
    productVariantId: number;
    quantity: number;
    location: string;
    stored: string;
    isFrozen?: boolean;
    shipmentId?: number;
    shipment?: Shipment | null;
    productVariantName?: string; // Optional derived field for UI
}

export interface Shipment {
    id: number;
    containerCode: string;
    origin: string;
    estimatedTimeOfArrival: string;
    status: string;
}

export const InventoryService = {
    // Basic fetches
    async getInventory(): Promise<InventoryItem[]> {
        const res = await fetch(`${API_BASE_URL}/inventory`); // Requires creating endpoint if missing
        if (!res.ok) throw new Error('Failed to fetch inventory');
        return res.json();
    },

    async getShipments(): Promise<Shipment[]> {
        const res = await fetch(`${API_BASE_URL}/shipments`); // Requires creating endpoint if missing
        if (!res.ok) throw new Error('Failed to fetch shipments');
        return res.json();
    },

    // Ghost sweep trigger
    async receiveShipment(id: number, productVariantId: number, quantityReceived: number, weightKg?: number): Promise<ApiMessage> {
        const res = await fetch(`${API_BASE_URL}/shipments/${id}/receive`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ productVariantId, quantityReceived, totalWeightKg: weightKg })
        });
        if (!res.ok) {
            const err = await res.text();
            throw new Error(err);
        }
        return res.json();
    },

    async registerWaste(id: number, quantityDumped: number, weightDumpedKg?: number, reason: string = "Spoilage"): Promise<ApiMessage> {
        const res = await fetch(`${API_BASE_URL}/inventory/${id}/waste`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ quantityDumped, weightDumpedKg, reason })
        });
        if (!res.ok) {
            const err = await res.text();
            throw new Error(err);
        }
        return res.json();
    },

    async freezeBatch(id: number): Promise<ApiMessage> {
        const res = await fetch(`${API_BASE_URL}/inventory/${id}/freeze`, { method: 'POST' });
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    },

    async unfreezeBatch(id: number): Promise<ApiMessage> {
        const res = await fetch(`${API_BASE_URL}/inventory/${id}/unfreeze`, { method: 'POST' });
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    }
};
