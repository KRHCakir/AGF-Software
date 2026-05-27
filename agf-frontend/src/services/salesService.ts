import axios from 'axios';

const API_Base = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5197/api';

export interface ParseOrderRequest {
    rawText: string;
    sellerId?: number;
}

export interface ParseOrderResponse {
    items: ParsedOrderItem[];
    warnings: string[];
    confidenceScore: number;
}

export interface ParsedOrderItem {
    originalTextSegment: string;
    productVariantId: number | null;
    productName: string | null;
    quantity: number;
    unit: string | null;
    estimatedPrice: number | null;
    confidence: number;
    currentStock?: number;
}

export interface ConfirmOrderRequest {
    customerId: number | null;
    customCustomerName: string | null;
    items: ConfirmOrderItem[];
}

export interface ConfirmOrderItem {
    productVariantId: number;
    quantity: number;
    agreedPrice?: number;
}

export const salesService = {
    parseOrder: async (text: string): Promise<ParseOrderResponse> => {
        const response = await axios.post<ParseOrderResponse>(`${API_Base}/sales/parse`, { rawText: text });
        return response.data;
    },

    confirmOrder: async (customerId: number | null, customCustomerName: string | null, items: ConfirmOrderItem[]) => {
        const response = await axios.post<number[]>(`${API_Base}/sales/confirm`, { customerId, customCustomerName, items });
        return response.data;
    }
};
