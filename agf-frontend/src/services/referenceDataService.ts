import { ApiError, getJson } from './apiClient';
import {
    CustomerOption,
    ProductOption,
    VendorOption,
    isNamedList,
    isProductList,
    toProductOption,
} from '../types/referenceData';

// Getypeerde loaders voor bestaande stamgegevens-endpoints (zelfde endpoints als de legacy-services,
// maar via apiConfig en zonder `any`). Alleen lezen.

const byName = <T extends { name: string }>(a: T, b: T) => a.name.localeCompare(b.name);

export async function getVendors(signal?: AbortSignal): Promise<VendorOption[]> {
    const json = await getJson('/incominginvoices/vendors', signal);
    if (!isNamedList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij leveranciers.');
    }
    return json.map(({ id, name }) => ({ id, name })).sort(byName);
}

export async function getCustomers(signal?: AbortSignal): Promise<CustomerOption[]> {
    const json = await getJson('/customers', signal);
    if (!isNamedList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij klanten.');
    }
    return json.map((item) => ({ id: item.id, name: item.name, searchCode: typeof item.searchCode === 'string' && item.searchCode !== '' ? item.searchCode : null, isActive: item.isActive !== false })).sort(byName);
}

export async function getProductsWithVariants(signal?: AbortSignal): Promise<ProductOption[]> {
    const json = await getJson('/products?includeVariants=true', signal);
    if (!isProductList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij producten.');
    }
    return json.map(toProductOption).filter((product) => product.variants.length > 0).sort(byName);
}
