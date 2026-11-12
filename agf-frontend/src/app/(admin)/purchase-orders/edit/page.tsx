'use client';

import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useRef, useState } from 'react';
import { ErrorState, type LoadState, LoadingRows } from '../../../../components/availability/StateViews';
import PlanSupplyForm, { type ReferenceData } from '../../../../components/supply/PlanSupplyForm';
import { useTranslation } from '../../../../i18n/LanguageContext';
import { formatDateTime } from '../../../../lib/availability';
import { isDefinitive } from '../../../../lib/purchaseOrderEdit';
import { orderNumbers } from '../../../../lib/purchaseOrderRules';
import { toApiError } from '../../../../services/apiClient';
import { getPurchaseOrder } from '../../../../services/purchaseOrderService';
import { getCustomers, getProductsWithVariants, getVendors } from '../../../../services/referenceDataService';
import type { PurchaseOrderDetail } from '../../../../types/purchaseOrder';

// Inkooporder aanpassen (stap 4a): hetzelfde formulier als Inkooporder aanmaken, gevuld met de opgeslagen order (?id=). Een definitieve
// order opent meteen op de order zelf: daar staat ze volledig alleen-lezen, met de melding en Correctie (met reden).
export default function PurchaseOrderEditPage() {
    const { t } = useTranslation();
    return (
        <Suspense fallback={<LoadingRows label={t('po_loading')} />}>
            <PurchaseOrderEdit />
        </Suspense>
    );
}

function PurchaseOrderEdit() {
    const { t, locale } = useTranslation();
    const router = useRouter();
    const value = useSearchParams().get('id');
    const id = value !== null && /^[1-9][0-9]{0,9}$/.test(value) ? Number(value) : null;
    const [state, setState] = useState<LoadState<{ order: PurchaseOrderDetail; reference: ReferenceData }>>({ status: 'loading' });
    const [token, setToken] = useState(0);

    useEffect(() => {
        if (id === null) return;
        const controller = new AbortController();
        setState({ status: 'loading' });
        Promise.all([getPurchaseOrder(id, controller.signal), getVendors(controller.signal), getCustomers(controller.signal), getProductsWithVariants(controller.signal)])
            .then(([order, vendors, customers, products]) => setState({ status: 'ready', data: { order, reference: { vendors, customers, products } } }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [id, token]);

    const back = id === null ? '/purchase-orders' : `/purchase-orders?id=${id}`;
    const definitive = state.status === 'ready' && isDefinitive(state.data.order);
    const redirected = useRef(false);
    useEffect(() => {
        if (!definitive || redirected.current) return;
        redirected.current = true;
        router.replace(back);
    }, [definitive, back, router]);
    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <h1 className="text-2xl font-bold text-gray-900" data-tour="page-title">{t('po_edit_title')}</h1>
                <Link href={back} className="text-sm font-medium text-blue-800 underline underline-offset-2">{t('po_edit_back')}</Link>
            </div>
            {id === null && <p role="alert" className="text-sm text-red-800">{t('po_not_found')}</p>}
            {state.status === 'loading' && id !== null && <LoadingRows rows={4} label={t('po_loading')} />}
            {state.status === 'error' && <ErrorState error={state.error} onRetry={() => setToken((n) => n + 1)} />}
            {state.status === 'ready' && (() => {
                const { order, reference } = state.data;
                if (isDefinitive(order)) {
                    return (
                        <p role="status" className="rounded border border-slate-300 bg-slate-50 px-3 py-2 text-sm text-slate-900" data-testid="po-edit-definitive">
                            {t('po_definitive_since', { date: order.finalizedAt ? formatDateTime(order.finalizedAt, locale) : '–', name: order.finalizedBy ?? '–' })}. {t('po_definitive_readonly')}
                        </p>
                    );
                }
                if (order.editable === false) return <p role="status" className="text-sm text-amber-900" data-testid="po-edit-not-editable">{t('po_edit_not_editable')}</p>;
                return (
                    <>
                        <p className="text-sm text-gray-700">{t('po_edit_intro', { number: orderNumbers(order).main })}</p>
                        <PlanSupplyForm key={order.id} route={order.route === 'DirectTrade' ? 'DirectTrade' : 'Warehouse'} reference={reference} onReceive={() => undefined}
                            editing={{ order, onSaved: (saved) => router.push(`/purchase-orders?id=${saved.id}`) }} />
                    </>
                );
            })()}
        </div>
    );
}
