import axios from 'axios';
import { getApiBaseUrl } from './apiConfig';

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
        const response = await axios.post<ParseOrderResponse>(`${getApiBaseUrl()}/sales/parse`, { rawText: text });
        return response.data;
    },

    confirmOrder: async (customerId: number | null, customCustomerName: string | null, items: ConfirmOrderItem[]) => {
        const response = await axios.post<number[]>(`${getApiBaseUrl()}/sales/confirm`, { customerId, customCustomerName, items });
        return response.data;
    }
};
