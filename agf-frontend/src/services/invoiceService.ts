/* eslint-disable @typescript-eslint/no-explicit-any */
export interface CreateManualInvoiceDto {
    customerId: number;
    invoiceDate: string; // ISO
    dueDate?: string;
    lines: CreateManualInvoiceLineDto[];
}

export interface CreateManualInvoiceLineDto {
    productVariantId?: number;
    description: string;
    quantity: number;
    unitPrice: number;
    vatPercentage: number;
}

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5197/api';

export const InvoiceService = {
    async createManual(data: CreateManualInvoiceDto): Promise<any> {
        const res = await fetch(`${API_BASE_URL}/invoice/manual`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data),
        });
        if (!res.ok) {
            const errorText = await res.text();
            throw new Error(`Failed to create invoice: ${errorText}`);
        }
        return res.json();
    },

    async getAll(): Promise<any[]> {
        const res = await fetch(`${API_BASE_URL}/invoice`);
        if (!res.ok) throw new Error('Failed to fetch invoices');
        return res.json();
    },

    async getCustomers(): Promise<any[]> {
        const res = await fetch(`${API_BASE_URL}/sales/customers`);
        if (!res.ok) throw new Error('Failed to fetch customers');
        return res.json();
    }
};
