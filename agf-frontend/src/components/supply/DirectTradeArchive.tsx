'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatQuantity } from '../../lib/availability';
import { toApiError } from '../../services/apiClient';
import { getJson } from '../../services/apiClient';
import { isSupplyCommitmentList, type SupplyCommitment } from '../../types/supply';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import OrderNumberText from '../purchase/OrderNumberText';

// Feedback 22 november: Direct trade is geen apart onderdeel meer. Directe handel is een gewone inkooporder met als ontvangstlocatie een
// locatie van de soort Onderweg (Inkooporder › Locatie en adressen). De klantgebonden aanvoer van vóór deze ronde blijft hier leesbaar
// (alleen lezen, niets verwijderd); toewijzingen en verzendingen staan zoals altijd op de verkooporder.
export default function DirectTradeArchive() {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<SupplyCommitment[]>>({ status: 'loading' });
    const [token, setToken] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        getJson('/supply-commitments?route=DirectTrade', controller.signal)
            .then((json) => {
                if (!isSupplyCommitmentList(json)) throw new Error('shape');
                setState({ status: 'ready', data: json.filter((commitment) => commitment.route === 'DirectTrade') });
            })
            .catch((caught: unknown) => {
                const error = toApiError(caught);
                if (error.kind !== 'aborted') setState({ status: 'error', error });
            });
        return () => controller.abort();
    }, [token]);

    return (
        <div className="mx-auto max-w-[96rem] space-y-4 p-2">
            <div>
                <h1 data-tour="page-title" className="text-2xl font-bold text-gray-900">{t('direct_trade_archive_title')}</h1>
                <p className="text-sm text-gray-600" data-testid="page-intro">{t('direct_trade_archive_intro')}</p>
                <p className="mt-1 text-sm"><Link href="/purchase-orders/new" className="font-medium text-blue-800 underline underline-offset-2">{t('direct_trade_archive_new')}</Link></p>
            </div>
            {state.status === 'loading' && <LoadingRows rows={4} label={t('supply_loading_sources')} />}
            {state.status === 'error' && <ErrorState error={state.error} onRetry={() => setToken((n) => n + 1)} />}
            {state.status === 'ready' && state.data.length === 0 && <p className="rounded border border-dashed border-gray-300 px-3 py-4 text-center text-sm text-gray-700" data-testid="direct-trade-archive-empty">{t('direct_trade_archive_empty')}</p>}
            {state.status === 'ready' && state.data.length > 0 && (
                <ul className="divide-y divide-gray-100 rounded border border-gray-200 bg-white text-sm" data-testid="direct-trade-archive">
                    {state.data.map((commitment) => (
                        <li key={commitment.id} className="px-3 py-2" data-testid={`direct-trade-archive-${commitment.id}`}>
                            <span className="flex flex-wrap gap-x-1.5">
                                <span className="font-semibold text-gray-900"><OrderNumberText id={commitment.id} orderNumber={commitment.orderNumber} supplierReference={commitment.supplierReference} /></span>
                                <span className="text-gray-700">· {commitment.vendorName ?? '–'} → {commitment.customerName ?? '–'}</span>
                                {commitment.destination && <span className="text-gray-700">· {commitment.destination}</span>}
                                {commitment.expectedAt && <span className="text-gray-700">· {formatDateTime(commitment.expectedAt, locale)}</span>}
                            </span>
                            <span className="block text-xs text-gray-600">
                                {commitment.lines.map((line) => `${line.productName ?? ''} ${line.productVariantName ?? ''} · ${formatQuantity(line.expectedQuantity, locale)}`).join(' — ')}
                            </span>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}
