'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { useTranslation } from '../../i18n/LanguageContext';
import { toApiError } from '../../services/apiClient';
import { getCustomers, getProductsWithVariants, getVendors } from '../../services/referenceDataService';
import { SupplyRoute } from '../../types/availability';
import { SupplyCommitment } from '../../types/supply';
import PlanSupplyForm, { ReferenceData } from './PlanSupplyForm';
import ReceiveOrder from './ReceiveOrder';
import ReceiveOrderPicker from './ReceiveOrderPicker';
import PurchaseOrderChain from '../purchase/PurchaseOrderChain';
import { SignedInAs } from '../shared/CurrentUserContext';
import PageTitle from '../shared/PageTitle';

export type SupplyTask = 'purchase-order' | 'receipt';

const TASKS: Record<SupplyTask, { titleKey: string; introKey: string; route: SupplyRoute }> = {
    'purchase-order': { titleKey: 'nav_purchase_order_new', introKey: 'intro_purchase_order_new', route: 'Warehouse' },
    receipt: { titleKey: 'nav_receipts', introKey: 'intro_receipts', route: 'Warehouse' },
};

// Eén scherm per taak, op dezelfde formulieren als de vroegere werkplek "Aanvoer": Inkooporder aanmaken (eigen magazijn), Ontvangst
// (pallets registreren tegen een inkooporder) en Klantgebonden aanvoer (DirectTrade: rechtstreeks naar de klant, nooit voorraad).
// Ontvangst met ?order=<id> toont "Dit wordt verwacht" van die order (vanaf de inkooporder of na het aanmaken), anders de open orders.
export default function SupplyWorkspace({ task, orderId = null }: { task: SupplyTask; orderId?: number | null }) {
    const { t } = useTranslation();
    const router = useRouter();
    const config = TASKS[task];
    const [reference, setReference] = useState<LoadState<ReferenceData>>({ status: 'loading' });
    const [referenceToken, setReferenceToken] = useState(0);

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

    // Na "Inkooporder aanmaken" meteen ontvangen: naar Ontvangst met deze order gekozen.
    const receiveFor = (commitment: SupplyCommitment) => router.push(`/receipts?order=${commitment.id}`);

    return (
        <div className="mx-auto max-w-[96rem] space-y-4 p-2">
            <div className="flex flex-wrap items-end justify-between gap-4">
                <div>
                    <PageTitle data-tour="page-title">{t(config.titleKey)}</PageTitle>
                    <p className="text-sm text-gray-600" data-testid="page-intro">{t(config.introKey)}</p>
                </div>
                <SignedInAs />
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
                    {orderId !== null && <PurchaseOrderChain orderIds={[orderId]} current="receipt" />}
                    {/* Feedback 22 november: ontvangen in één scherm. Met een order "Dit wordt verwacht", anders eerst een open order kiezen. */}
                    {orderId !== null ? <ReceiveOrder key={orderId} orderId={orderId} /> : <ReceiveOrderPicker />}
                </>
            )}
        </div>
    );
}
