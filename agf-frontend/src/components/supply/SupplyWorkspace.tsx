'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { useTranslation } from '../../i18n/LanguageContext';
import { toApiError } from '../../services/apiClient';
import { getCustomers, getProductsWithVariants, getVendors } from '../../services/referenceDataService';
import { getSupplyCommitment } from '../../services/supplyService';
import { SupplyRoute } from '../../types/availability';
import { SupplyCommitment } from '../../types/supply';
import { inputClass } from './FieldError';
import PlanSupplyForm, { ReferenceData } from './PlanSupplyForm';
import ReceivePalletForm from './ReceivePalletForm';
import { useActorReference } from './useActorReference';

export type SupplyTask = 'purchase-order' | 'receipt' | 'direct-trade';

const TASKS: Record<SupplyTask, { titleKey: string; introKey: string; route: SupplyRoute }> = {
    'purchase-order': { titleKey: 'nav_purchase_order_new', introKey: 'intro_purchase_order_new', route: 'Warehouse' },
    receipt: { titleKey: 'nav_receipts', introKey: 'intro_receipts', route: 'Warehouse' },
    'direct-trade': { titleKey: 'nav_direct_trade', introKey: 'intro_direct_trade', route: 'DirectTrade' },
};

// Eén scherm per taak, op dezelfde formulieren als de vroegere werkplek "Aanvoer": Inkooporder aanmaken (eigen magazijn), Ontvangst
// (pallets registreren tegen een inkooporder) en Klantgebonden aanvoer (DirectTrade: rechtstreeks naar de klant, nooit voorraad).
// Ontvangst kan met ?order=<id> een inkooporder alvast kiezen (vanaf de inkooporder of na het aanmaken).
export default function SupplyWorkspace({ task, orderId = null }: { task: SupplyTask; orderId?: number | null }) {
    const { t } = useTranslation();
    const router = useRouter();
    const [actor, setActor] = useActorReference();
    const config = TASKS[task];
    const [reference, setReference] = useState<LoadState<ReferenceData>>({ status: 'loading' });
    const [referenceToken, setReferenceToken] = useState(0);
    const [preselected, setPreselected] = useState<SupplyCommitment | null>(null);
    const [orderError, setOrderError] = useState<string | null>(null);

    useEffect(() => {
        if (task === 'receipt') return;
        const controller = new AbortController();
        setReference({ status: 'loading' });
        Promise.all([getVendors(controller.signal), getCustomers(controller.signal), getProductsWithVariants(controller.signal)])
            .then(([vendors, customers, products]) => setReference({ status: 'ready', data: { vendors, customers, products } }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setReference({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [task, referenceToken]);

    useEffect(() => {
        if (task !== 'receipt' || orderId === null) return;
        const controller = new AbortController();
        setOrderError(null);
        getSupplyCommitment(orderId, controller.signal)
            .then(setPreselected)
            .catch((caught: unknown) => { if (toApiError(caught).kind !== 'aborted') setOrderError(toApiError(caught).message); });
        return () => controller.abort();
    }, [task, orderId]);

    // Na "Inkooporder aanmaken" meteen ontvangen: naar Ontvangst met deze order gekozen.
    const receiveFor = (commitment: SupplyCommitment) => router.push(`/receipts?order=${commitment.id}`);
    const consumePreselected = useCallback(() => setPreselected(null), []);

    return (
        <div className="mx-auto max-w-[96rem] space-y-4 p-2">
            <div className="flex flex-wrap items-end justify-between gap-4">
                <div>
                    <h1 data-tour="page-title" className="text-2xl font-bold text-gray-900">{t(config.titleKey)}</h1>
                    <p className="text-sm text-gray-600" data-testid="page-intro">{t(config.introKey)}</p>
                </div>
                <div className="w-64">
                    <label htmlFor="actorReference-page" className="text-xs font-medium text-gray-700">{t('supply_actor')}</label>
                    <input id="actorReference-page" value={actor} maxLength={100} onChange={(event) => setActor(event.target.value)}
                        placeholder={t('supply_actor_placeholder')} className={inputClass} aria-describedby="actorReference-hint" />
                    <span id="actorReference-hint" className="block text-xs text-gray-500">{t('supply_actor_hint')}</span>
                </div>
            </div>

            {task !== 'receipt' && (
                <>
                    {reference.status === 'loading' && <LoadingRows rows={4} label={t('supply_loading_reference')} />}
                    {reference.status === 'error' && <ErrorState error={reference.error} onRetry={() => setReferenceToken((n) => n + 1)} />}
                    {reference.status === 'ready' && <PlanSupplyForm key={config.route} route={config.route} reference={reference.data} onReceive={receiveFor} />}
                </>
            )}

            {task === 'receipt' && (
                <>
                    {orderError && <p role="alert" className="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-900">{orderError}</p>}
                    <ReceivePalletForm actor={actor} preselected={preselected} onPreselectedConsumed={consumePreselected} />
                </>
            )}
        </div>
    );
}
