import { ApiMessage } from './apiTypes';
import { getApiBaseUrl } from './apiConfig';

export interface OrderLineDto {
    id: number;
    productVariantId: number;
    productName: string;
    origin: string;
    quantity: number;
    unitPrice: number;
    lineTotal: number;
}

export interface OrderDto {
    id: number;
    customerId: number;
    customerName: string;
    totalAmount: number;
    containerCode?: string;
    status: string;
    createdAt: string;
    lines: OrderLineDto[];
}

export interface CreateOrderLineDto {
    productVariantId: number;
    quantity: number;
    unitPrice?: number;
}

export interface CreateOrderDto {
    customerId?: number | null; // Maak optioneel
    customerName?: string;      // Voeg deze toe
    containerCode?: string;
    lines: {
        productVariantId: number;
        quantity: number;
        unitPrice?: number;
    }[];
}

export const OrderService = {
    async getAll(): Promise<OrderDto[]> {
        const res = await fetch(`${getApiBaseUrl()}/order`);
        if (!res.ok) throw new Error('Failed to fetch orders');
        return res.json();
    },

    async create(data: CreateOrderDto): Promise<OrderDto> {
        const res = await fetch(`${getApiBaseUrl()}/order`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data),
        });
        if (!res.ok) {
            const errorText = await res.text();
            throw new Error(`Failed to create order: ${errorText}`);
        }
        return res.json();
    },

    async generateInvoice(id: number): Promise<{ invoiceNumber: string }> {
        const res = await fetch(`${getApiBaseUrl()}/order/${id}/generate-invoice`, {
            method: 'POST',
        });
        if (!res.ok) {
            const errorText = await res.text();
            throw new Error(`Failed to generate invoice: ${errorText}`);
        }
        return res.json();
    },

    async deleteOrder(id: number): Promise<ApiMessage> {
        const res = await fetch(`${getApiBaseUrl()}/order/${id}`, {
            method: 'DELETE',
        });
        if (!res.ok) {
            const errorText = await res.text();
            throw new Error(`Failed to delete order: ${errorText}`);
        }
        return res.json();
    }
};
