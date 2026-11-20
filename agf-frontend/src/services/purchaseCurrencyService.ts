import { ApiError, getJson, putJson } from './apiClient';

// Valuta's om uit te kiezen bij inkoop (valuta-ronde; lezen mag iedereen, wijzigen met Beheer). EUR staat altijd vooraan. Per valuta of
// de ECB er een dagkoers voor publiceert; voor de andere vult de gebruiker de koers zelf in.
export interface PurchaseCurrency {
    code: string;
    hasEcbRate: boolean;
}

const read = (json: unknown): PurchaseCurrency[] => {
    const list = (json as { currencies?: unknown } | null)?.currencies;
    if (!Array.isArray(list) || !list.every((item) => typeof item?.code === 'string' && /^[A-Z]{3}$/.test(item.code) && typeof item.hasEcbRate === 'boolean')) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de valuta\'s.');
    }
    return list.map((item) => ({ code: item.code as string, hasEcbRate: item.hasEcbRate as boolean }));
};

export async function getPurchaseCurrencies(signal?: AbortSignal): Promise<PurchaseCurrency[]> {
    return read(await getJson('/settings/purchase-currencies', signal));
}

export async function updatePurchaseCurrencies(codes: string[]): Promise<PurchaseCurrency[]> {
    return read((await putJson('/settings/purchase-currencies', { codes })).json);
}
