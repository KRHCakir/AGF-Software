'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { ErrorState, LoadState, LoadingRows } from '../../../../components/availability/StateViews';
import PurchaseInvoiceForm from '../../../../components/purchase/PurchaseInvoiceForm';
import PurchaseInvoiceSettingsPanel from '../../../../components/purchase/PurchaseInvoiceSettingsPanel';
import { useTranslation } from '../../../../i18n/LanguageContext';
import { toApiError } from '../../../../services/apiClient';
import { getPurchaseInvoiceSettings, getPurchaseVendors } from '../../../../services/purchaseInvoiceService';
import { getProductsWithVariants } from '../../../../services/referenceDataService';
import type { ProductOption } from '../../../../types/referenceData';
import type { PurchaseInvoiceSettings, PurchaseVendor } from '../../../../types/purchaseInvoice';

interface Reference {
    vendors: PurchaseVendor[];
    products: ProductOption[];
    settings: PurchaseInvoiceSettings;
}

// Inkoopfactuur invoeren: één compact scherm. Referentiedata (leveranciers, artikelen, instellingen) wordt los van het
// concept geladen en ververst: het formulier bewaart zijn eigen waarden en wordt daardoor nooit overschreven.
export default function NewPurchaseInvoicePage() {
    const { t } = useTranslation();
    const [state, setState] = useState<LoadState<Reference>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);
    const [settingsOpen, setSettingsOpen] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([getPurchaseVendors(controller.signal), getProductsWithVariants(controller.signal), getPurchaseInvoiceSettings(controller.signal)])
            .then(([vendors, products, settings]) => setState({ status: 'ready', data: { vendors, products, settings } }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [reloadToken]);

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('purchase_new_title')}</h1>
                    <p className="text-sm text-gray-600">{t('purchase_new_subtitle')}</p>
                </div>
                <div className="flex items-center gap-2 text-sm">
                    <Link href="/purchase-invoices" className="font-medium text-blue-800 underline underline-offset-2">{t('purchase_back_to_list')}</Link>
                    {/* Discreet tandwiel: bedrijfsinstellingen, nooit naast Opslaan. */}
                    <button type="button" tabIndex={-1} aria-label={t('purchase_settings_title')} title={t('purchase_settings_title')} onClick={() => setSettingsOpen((open) => !open)}
                        className="rounded border border-transparent px-2 py-1 text-gray-500 hover:border-gray-300 hover:text-gray-800">⚙</button>
                </div>
            </div>

            {state.status === 'loading' && <LoadingRows label={t('purchase_loading')} />}
            {state.status === 'error' && <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {state.status === 'ready' && (
                <>
                    {settingsOpen && (
                        <PurchaseInvoiceSettingsPanel settings={state.data.settings} onClose={() => setSettingsOpen(false)}
                            onSaved={(settings) => setState({ status: 'ready', data: { ...state.data, settings } })} />
                    )}
                    <PurchaseInvoiceForm vendors={state.data.vendors} products={state.data.products} settings={state.data.settings} />
                </>
            )}
        </div>
    );
}
