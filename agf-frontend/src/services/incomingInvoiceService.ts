/* eslint-disable @typescript-eslint/no-explicit-any */
import { IncomingInvoice } from '../types/incomingInvoice';

const API_BASE_URL = 'http://localhost:5197/api'; // Correct port from launchSettings.json

export const IncomingInvoiceService = {
    async getAll(): Promise<IncomingInvoice[]> {
        const res = await fetch(`${API_BASE_URL}/incominginvoices`);
        if (!res.ok) throw new Error('Failed to fetch invoices');
        return res.json();
    },

    async getVendors(): Promise<any[]> {
        const res = await fetch(`${API_BASE_URL}/incominginvoices/vendors`);
        if (!res.ok) throw new Error('Failed to fetch vendors');
        return res.json();
    },

    async getById(id: number): Promise<IncomingInvoice> {
        const res = await fetch(`${API_BASE_URL}/incominginvoices/${id}`);
        if (!res.ok) throw new Error('Failed to fetch invoice');
        return res.json();
    },

    async update(id: number, data: IncomingInvoice): Promise<void> {
        const res = await fetch(`${API_BASE_URL}/incominginvoices/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data),
        });
        if (!res.ok) throw new Error('Failed to update invoice');
    },

    async approve(id: number): Promise<void> {
        const res = await fetch(`${API_BASE_URL}/incominginvoices/${id}/approve`, { method: 'POST' });
        if (!res.ok) throw new Error('Failed to approve invoice');
    },

    async reject(id: number): Promise<void> {
        const res = await fetch(`${API_BASE_URL}/incominginvoices/${id}/reject`, { method: 'POST' });
        if (!res.ok) throw new Error('Failed to reject invoice');
    },

    async createManual(data: any): Promise<any> {
        const res = await fetch(`${API_BASE_URL}/incominginvoices/manual`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data),
        });
        if (!res.ok) {
            const errorText = await res.text();
            throw new Error(`Failed to create manual invoice: ${errorText}`);
        }
        return res.json();
    }
};
