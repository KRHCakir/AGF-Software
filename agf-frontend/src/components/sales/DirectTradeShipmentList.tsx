'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { formatDateOnly } from '../../lib/calendarDate';
import { toApiError } from '../../services/apiClient';
import { listDirectTradeShipments } from '../../services/salesOrderService';
import { DirectTradeShipment } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { DirectTradeCorrectedBadge, DirectTradeShippedBadge } from './ReservationBadges';
import { secondaryButtonClass } from '../supply/SuccessPanel';

const th = 'px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-violet-900';
const td = 'px-2 py-1.5 align-top';

// DirectTrade-verzendingen van één order, gescheiden van de magazijnverzendingen. Klant, leverancier, herkomst en bestemming zijn de
// snapshots van het moment van verzenden. Er is geen afleverbevestiging: het systeem weet alleen dat die nog niet is vastgelegd.
// Een teruggenomen verzending blijft zichtbaar met haar oorspronkelijke regels; de correctie (reden, actor, tijd) staat erbij.
export default function DirectTradeShipmentList({ orderId, token, onShipments, canCorrect = false, correctingId = null, onCorrect }: {
    orderId: number;
    token: number;
    onShipments?: (shipments: DirectTradeShipment[]) => void;
    canCorrect?: boolean;
    correctingId?: number | null;
    onCorrect?: (shipment: DirectTradeShipment) => void;
}) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<DirectTradeShipment[]>>({ status: 'loading' });
    const [retry, setRetry] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        listDirectTradeShipments(orderId, controller.signal)
            .then((data) => { setState({ status: 'ready', data }); onShipments?.(data); })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [orderId, token, retry, onShipments]);

    if (state.status === 'loading') return <LoadingRows rows={2} label={t('sales_ship_list_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setRetry((n) => n + 1)} />;
    if (state.data.length === 0) return <p className="text-sm text-gray-700">{t('sales_ship_list_empty')}</p>;

    return (
        <div className="space-y-3">
            {state.data.map((shipment) => (
                <article key={shipment.id} aria-labelledby={`dt-shipment-${shipment.id}`} className="rounded border border-violet-200 bg-white">
                    <header className="flex flex-wrap items-center gap-2 border-b border-violet-100 bg-violet-50 px-3 py-2 text-sm">
                        <h4 id={`dt-shipment-${shipment.id}`} className="font-semibold text-violet-950">{t('sales_ship_heading', { id: String(shipment.id) })}</h4>
                        {shipment.correction ? <DirectTradeCorrectedBadge /> : <DirectTradeShippedBadge />}
                        <span>{t('sales_ship_shipped_on', { date: formatDateOnly(shipment.shippedOn, locale) })}</span>
                        <span className="text-gray-700">{shipment.customerName}</span>
                        {shipment.reference && <span className="font-mono text-xs">{shipment.reference}</span>}
                        <span className="ml-auto text-xs text-gray-600">{formatDateTime(shipment.recordedAt, locale)} · {shipment.actorReference}</span>
                        {!shipment.correction && onCorrect && (
                            <button type="button" className={`${secondaryButtonClass} border-amber-300 text-amber-900 hover:bg-amber-50`} disabled={!canCorrect || correctingId === shipment.id}
                                onClick={() => onCorrect(shipment)}>{t('sales_dt_correct')}</button>
                        )}
                    </header>
                    {shipment.correction && (
                        <div role="note" className="border-b border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-950">
                            <p className="font-medium">{t('sales_dt_corrected_by', { date: formatDateTime(shipment.correction.correctedAt, locale), actor: shipment.correction.actorReference })}</p>
                            <p className="mt-0.5">“{shipment.correction.confirmationStatement}” · <span className="italic">{shipment.correction.reason}</span></p>
                        </div>
                    )}
                    <table className="min-w-full divide-y divide-violet-100 text-sm">
                        <caption className="sr-only">{t('sales_ship_heading', { id: String(shipment.id) })}</caption>
                        <thead>
                            <tr>
                                <th scope="col" className={th}>{t('sales_col_source')}</th>
                                <th scope="col" className={th}>{t('supply_variant')}</th>
                                <th scope="col" className={th}>{t('availability_col_lot')}</th>
                                <th scope="col" className={`${th} text-right`}>{t('supply_quantity')}</th>
                                <th scope="col" className={`${th} text-right`}>{t('sales_ship_kg')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-violet-50">
                            {shipment.lines.map((line) => (
                                <tr key={line.id} className={shipment.correction ? 'text-gray-500' : undefined}>
                                    <td className={td}>{line.vendorName} · {line.origin} → {line.destination ?? t('availability_unknown')}</td>
                                    <td className={td}>{line.productName} · {line.productVariantName}</td>
                                    <td className={td}>
                                        {line.lotNumber}
                                        {line.lotDeviationReason && (
                                            <span className="block text-xs text-amber-900">{t('sales_ship_lot_deviation_note', { source: line.sourceLotNumber ?? '', reason: line.lotDeviationReason })}</span>
                                        )}
                                    </td>
                                    <td className={`${td} text-right`}>{formatQuantity(line.quantity, locale)} <UnitBadge unit={line.unit} /></td>
                                    <td className={`${td} text-right`}>{formatKg(line.netWeightKg, locale)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </article>
            ))}
        </div>
    );
}
