'use client';

import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { ErrorState, LoadingRows, type LoadState } from '../../../../components/availability/StateViews';
import DirectInvoiceForm from '../../../../components/salesInvoice/DirectInvoiceForm';
import PageTitle from '../../../../components/shared/PageTitle';
import { useTranslation } from '../../../../i18n/LanguageContext';
import { toApiError } from '../../../../services/apiClient';
import { listCostArticles } from '../../../../services/costArticleService';
import { getCustomers, getProductsWithVariants } from '../../../../services/referenceDataService';
import { getRevenueTypes } from '../../../../services/salesInvoiceService';
import type { CostArticle } from '../../../../types/costArticle';
import type { CustomerOption, ProductOption } from '../../../../types/referenceData';
import type { RevenueType } from '../../../../types/salesInvoice';

interface Reference { customers: CustomerOption[]; products: ProductOption[]; services: CostArticle[]; revenueTypes: RevenueType[] }

// Verkoop › Verkoopfacturen › Nieuwe factuur (verkoopfactuur 1): een directe factuur zonder order. Na opslaan het concept in de lijst.
export default function NewSalesInvoicePage() {
    const { t } = useTranslation();
    const router = useRouter();
    const [token, setToken] = useState(0);
    const [reference, setReference] = useState<LoadState<Reference>>({ status: 'loading' });

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([getCustomers(controller.signal), getProductsWithVariants(controller.signal), listCostArticles(false, controller.signal), getRevenueTypes(false, controller.signal)])
            .then(([customers, products, services, revenueTypes]) => setReference({ status: 'ready', data: { customers, products, services, revenueTypes } }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setReference({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [token]);

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div>
                <PageTitle data-tour="page-title">{t('si_new_direct')}</PageTitle>
                <p className="text-sm text-gray-600" data-testid="page-intro">{t('si_new_direct_intro')}</p>
            </div>
            {reference.status === 'loading' && <LoadingRows rows={4} label={t('si_loading')} />}
            {reference.status === 'error' && <ErrorState error={reference.error} onRetry={() => setToken((n) => n + 1)} />}
            {reference.status === 'ready' && (
                <DirectInvoiceForm customers={reference.data.customers} products={reference.data.products} serviceArticles={reference.data.services}
                    revenueTypes={reference.data.revenueTypes} onCreated={(invoice) => router.push(`/sales-invoices?id=${invoice.id}`)} onCancel={() => router.push('/sales-invoices')} />
            )}
        </div>
    );
}
