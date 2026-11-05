import { randomUUID } from 'node:crypto';
import { expect, type APIRequestContext } from '@playwright/test';

// Vaste testgegevens voor de flows naast de rondleiding (toetsenbord, verkoopketen): leverancier "Nature's Pride", klant "Lokaal Fruit
// B.V." en een complete Avocado Hass. Alleen aanmaken als ze ontbreken (lege database), via de gewone API. Deze specs draaien na de
// rondleiding (playwright.tour.config.ts), zodat die zelf op een echt lege database begint.

const API = process.env.TOUR_API_URL ?? 'http://localhost:5198/api';

async function get<T>(request: APIRequestContext, path: string): Promise<T> {
    const response = await request.get(`${API}${path}`);
    expect(response.ok(), `GET ${path}`).toBeTruthy();
    return (await response.json()) as T;
}

async function post(request: APIRequestContext, path: string, data: unknown) {
    const response = await request.post(`${API}${path}`, { data });
    expect(response.ok(), `POST ${path}: ${await response.text()}`).toBeTruthy();
}

export async function ensureFlowMasterData(request: APIRequestContext) {
    const vendors = await get<{ name: string; isActive: boolean }[]>(request, '/purchase-invoices/vendors');
    if (!vendors.some((vendor) => vendor.isActive && /^nature/i.test(vendor.name))) {
        await post(request, '/relations', { requestId: randomUUID(), searchCode: 'NATURE', name: "Nature's Pride", preferredLanguage: 'nl', vendor: {} });
    }
    const customers = await get<{ name: string; isActive?: boolean }[]>(request, '/customers');
    if (!customers.some((customer) => customer.isActive !== false && /^lo/i.test(customer.name))) {
        await post(request, '/relations', { requestId: randomUUID(), searchCode: 'LOKAAL', name: 'Lokaal Fruit B.V.', preferredLanguage: 'nl', customer: {} });
    }
    type Variant = { name: string; shortcutCode: string | null; packagingUnit: string | null; defaultNetKgPerPackage: number | null; isArchived?: boolean };
    const products = await get<{ name: string; variants: Variant[] }[]>(request, '/products?includeVariants=true');
    const hass = (products.find((product) => product.name.toLowerCase() === 'avocado')?.variants ?? []).filter((variant) => variant.name.toLowerCase().startsWith('hass'));
    if (!hass.some((variant) => variant.shortcutCode && variant.packagingUnit && variant.defaultNetKgPerPackage && !variant.isArchived)) {
        await post(request, '/articles', {
            productName: 'Avocado', variantName: 'Hass', shortcutCode: 'AVOPE18', originCountryCode: 'PE', sizeCategory: '18', tradeClass: 'I',
            packaging: '4 kg doos', packagingUnit: 'colli', weightMethod: 'Calculated', defaultNetKgPerPackage: 4, defaultPackagesPerPallet: 240,
        });
    }
}
