import { ApiMessage } from './apiTypes';

const API_BASE_URL = 'http://localhost:5197/api';

export const ProductService = {
    async getAll(): Promise<Product[]> {
        const res = await fetch(`${API_BASE_URL}/products?includeVariants=true`);
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    },

    async updateShortcut(variantId: number, shortcutCode: string | null): Promise<ApiMessage> {
        const res = await fetch(`${API_BASE_URL}/products/variants/${variantId}/shortcut`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ shortcutCode })
        });
        if (!res.ok) {
            const err = await res.json();
            throw new Error(err.message || 'Failed to update shortcut');
        }
        return res.json();
    },
    async quickAddShortcut(data: {
        productName: string;
        variantName?: string;
        origin?: string;
        season?: string; // <-- Toevoegen
        notes?: string; // <-- Toevoegen
        sizeCategory?: string;
        shortcutCode: string;
    }): Promise<ApiMessage & { variantId: number }> {
        const res = await fetch(`${API_BASE_URL}/products/quick-shortcut`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        });
        if (!res.ok) {
            const err = await res.text();
            throw new Error(err || 'Failed to create shortcut');
        }
        return res.json();
    }
};

export interface Product {
    id: number;
    name: string;
    imageUrl?: string;
    variants: ProductVariant[];
}

export interface ProductVariant {
    id: number;
    productId: number;
    name: string;
    origin: string;
    season?: string;
    notes?: string;
    packaging: string;
    sizeCategory?: string;
    defaultPrice: number;
    shortcutCode?: string;
}
