'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatQuantity } from '../../lib/availability';
import { todayInAmsterdam } from '../../lib/calendarDate';
import { orderListGroups } from '../../lib/receiptOrderList';
import { toApiError } from '../../services/apiClient';
import { listOpenWarehouseCommitments } from '../../services/supplyService';
import type { SupplyCommitment } from '../../types/supply';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { FirstStepLink } from '../shared/FirstStep';
import OrderNumberText from '../purchase/OrderNumberText';
import { inputClass } from './FieldError';

// Ontvangen zonder gekozen order: de open inkooporders (eerst wat vandaag of eerder verwacht werd), zoeken op leverancier, ordernummer,
// partij en product. Een order kiezen opent "Dit wordt verwacht" (/receipts?order=…).
export default function ReceiveOrderPicker() {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<SupplyCommitment[]>>({ status: 'loading' });
    const [token, setToken] = useState(0);
    const [search, setSearch] = useState('');

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        listOpenWarehouseCommitments(controller.signal)
            .then((data) => setState({ status: 'ready', data }))
            .catch((caught: unknown) => {
                const error = toApiError(caught);
                if (error.kind !== 'aborted') setState({ status: 'error', error });
            });
        return () => controller.abort();
    }, [token]);

    if (state.status === 'loading') return <LoadingRows rows={4} label={t('supply_loading_sources')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setToken((n) => n + 1)} />;
    if (state.data.length === 0) {
        // Ontvangen gaat altijd tegen een inkooporder: zonder open order is de eerste stap er een aanmaken.
        return <EmptyState testId="receipts-empty" title={t('receipts_empty_title')} description={t('receipts_empty_text')}
            action={<FirstStepLink href="/purchase-orders/new" label={t('nav_purchase_order_new')} />} />;
    }
    const groups = orderListGroups(state.data, search, todayInAmsterdam());
    return (
        <section aria-labelledby="receipt-orders" className="space-y-2 rounded-md border border-gray-300 bg-white p-4">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
                <div>
                    <h2 id="receipt-orders" className="font-semibold text-gray-900">{t('receipt_choose_order')}</h2>
                    <p className="text-sm text-gray-600">{t('receipt_choose_order_hint')}</p>
                </div>
                <label className="block text-xs font-medium text-gray-700">
                    {t('supply_receive_search')}
                    <input type="search" autoFocus data-tour="receive-search" value={search} onChange={(event) => setSearch(event.target.value)}
                        placeholder={t('supply_receive_search_placeholder')} className={`${inputClass} w-full sm:w-80`} />
                </label>
            </div>
            {groups.length === 0 && <p className="rounded border border-dashed border-gray-300 px-3 py-4 text-center text-sm text-gray-700">{t('supply_receive_no_matches')}</p>}
            <ul className="divide-y divide-gray-100 rounded border border-gray-200">
                {groups.map(({ commitment, lines, due }) => (
                    <li key={commitment.id}>
                        <Link href={`/receipts?order=${commitment.id}`} data-tour="receive-order" data-tour-key={commitment.id}
                            className="block px-3 py-2 hover:bg-blue-50 focus:bg-blue-50 focus:outline-none focus:ring-2 focus:ring-blue-500">
                            <span className="flex flex-wrap items-center gap-x-1.5 text-sm">
                                <span className="font-semibold text-gray-900"><OrderNumberText id={commitment.id} orderNumber={commitment.orderNumber} supplierReference={commitment.supplierReference} /></span>
                                <span className="text-gray-700">· {commitment.vendorName ?? '–'}</span>
                                {commitment.expectedAt && <span className="text-gray-700">· {t('supply_expected_on', { date: formatDateTime(commitment.expectedAt, locale) })}</span>}
                                {due && <span className="rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-900">{t('receive_order_due')}</span>}
                            </span>
                            <span className="block text-xs text-gray-600">
                                {lines.map((line) => `${line.productName ?? ''} ${line.productVariantName ?? ''} · ${formatQuantity(line.expectedQuantity, locale)}`).join(' — ')}
                            </span>
                        </Link>
                    </li>
                ))}
            </ul>
        </section>
    );
}
