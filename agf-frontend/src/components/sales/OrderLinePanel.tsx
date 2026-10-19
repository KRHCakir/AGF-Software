'use client';

import { useCallback, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { ReserveCommand, blockedBy, releaseKey, reserveKey } from '../../lib/pendingCommands';
import { lineProgress } from '../../lib/salesOrderRules';
import { toApiError } from '../../services/apiClient';
import { AvailabilitySnapshot, getAvailabilitySnapshot } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { ReservableStockLine, SalesOrder, SalesOrderLine, StockReservation } from '../../types/salesOrder';
import QualityBadge from '../availability/QualityBadge';
import { UnitBadge } from '../availability/QuantityCell';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { usePendingCommands } from './PendingCommandsContext';
import ReleaseReservationForm from './ReleaseReservationForm';
import { OnHoldBadge, ReservationStatusBadge } from './ReservationBadges';
import ReservableStockTable from './ReservableStockTable';
import ReserveForm from './ReserveForm';

const th = 'px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-gray-600';
const td = 'px-2 py-1.5 align-top';

interface Notice {
    kind: 'reserved' | 'released';
    replayed: boolean;
    snapshot: AvailabilitySnapshot | null | 'failed';
}

// Eén orderregel: klantvraag, voortgang, bestaande reserveringen (met vrijgave) en het reserveren op exacte voorraad.
// Na elke geslaagde schrijfactie worden de order, de reserveerbare voorraad en de beschikbaarheid opnieuw geladen.
// Een onafgerond commando op deze regel wordt altijd getoond en blokkeert elk nieuw commando op dezelfde regel.
export default function OrderLinePanel({ order, line, actor, readOnly, onOrderChanged }: {
    order: SalesOrder;
    line: SalesOrderLine;
    actor: string;
    readOnly: boolean;
    onOrderChanged: () => Promise<boolean>;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const [choosing, setChoosing] = useState(false);
    const [selected, setSelected] = useState<ReservableStockLine | null>(null);
    const [releasingId, setReleasingId] = useState<number | null>(null);
    const [stockToken, setStockToken] = useState(0);
    // Elke herlaadactie na een conflict geeft de formulieren een nieuwe sleutel; een bewaard commando komt daarbij gewoon terug.
    const [decisionToken, setDecisionToken] = useState(0);
    const [notice, setNotice] = useState<Notice | null>(null);
    const [reloadFailed, setReloadFailed] = useState(false);

    const progress = lineProgress(line);
    const lineBlock = blockedBy(commands, { kind: 'line', orderId: order.id, lineId: line.id });
    const pendingReserve = (commands[reserveKey(order.id, line.id)] as ReserveCommand | undefined) ?? null;
    const canStartReserve = !readOnly && line.openQuantity > 0 && lineBlock === null;
    const showChooser = choosing || pendingReserve !== null;

    const reload = useCallback(async () => {
        setReloadFailed(false);
        const ok = await onOrderChanged();
        if (!ok) setReloadFailed(true);
        setStockToken((n) => n + 1);
        setDecisionToken((n) => n + 1);
        setSelected(null);
        setReleasingId(null);
    }, [onOrderChanged]);

    const afterWrite = async (kind: Notice['kind'], replayed: boolean) => {
        await reload();
        let snapshot: Notice['snapshot'] = 'failed';
        try {
            snapshot = await getAvailabilitySnapshot(line.productVariantId, line.unit);
        } catch (caught) {
            void toApiError(caught);
            snapshot = 'failed';
        }
        setNotice({ kind, replayed, snapshot });
    };

    const onReserved = async (saved: SaveResult<StockReservation>) => {
        setChoosing(false);
        await afterWrite('reserved', saved.replayed);
    };

    const onReleased = async () => {
        await afterWrite('released', false);
    };

    // Eén stabiele callback (geen herladen van de tabel zodra een commando ontstaat of afloopt): bij een onafgeronde
    // reservering volgt de keuze de inhoudsregel van dat commando; anders vervalt een keuze die niet meer ongewijzigd in de lijst staat.
    const pendingContentId = useRef<number | null>(null);
    pendingContentId.current = pendingReserve?.payload.palletContentLineId ?? null;
    const onStockLoaded = useCallback((rows: ReservableStockLine[]) => {
        setSelected((current) => {
            if (pendingContentId.current !== null) {
                return rows.find((row) => row.palletContentLineId === pendingContentId.current) ?? null;
            }
            if (current === null) return null;
            const fresh = rows.find((row) => row.palletContentLineId === current.palletContentLineId);
            return fresh && fresh.contentLineVersion === current.contentLineVersion
                && fresh.availableQuantity === current.availableQuantity && fresh.availableNetWeightKg === current.availableNetWeightKg
                ? current
                : null;
        });
    }, []);

    // Het reserveerformulier hoort bij één inhoudsregel: die van het onafgeronde commando, anders de gekozen rij.
    // De sleutel hangt alleen van die inhoudsregel af, niet van het bestaan van een commando: zo blijft dezelfde
    // formulierinstantie staan tijdens het versturen en bij de afhandeling (bevriezen, 400/409, onbekende uitkomst).
    const formContentId = pendingReserve?.payload.palletContentLineId ?? selected?.palletContentLineId ?? null;
    const formStock = formContentId !== null && selected?.palletContentLineId === formContentId ? selected : null;

    const reservations = [...line.reservations].sort((a, b) => Number(b.status === 'Active') - Number(a.status === 'Active') || b.id - a.id);

    const statusClass = progress.ready ? 'text-green-800' : progress.complete ? 'text-red-800' : 'text-amber-900';
    const statusText = progress.ready ? t('sales_line_complete') : progress.complete ? t('sales_line_complete_on_hold') : t('sales_line_open', { quantity: formatQuantity(line.openQuantity, locale) });
    const barClass = progress.ready ? 'bg-green-600' : progress.complete ? 'bg-red-600' : 'bg-blue-600';

    return (
        <section aria-labelledby={`line-${line.id}`} className="rounded-md border border-gray-300 bg-white">
            <header className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b border-gray-200 bg-gray-50 px-3 py-2 text-sm">
                <h3 id={`line-${line.id}`} className="font-semibold text-gray-900">{line.productName} · {line.productVariantName}</h3>
                <UnitBadge unit={line.unit} />
                <span className="text-gray-800">{t('sales_line_demand', { quantity: formatQuantity(line.quantity, locale) })}</span>
                <span className="text-gray-800">
                    {t('sales_line_reserved', { quantity: formatQuantity(line.reservedQuantity, locale), kg: formatKg(line.reservedNetWeightKg, locale) })}
                </span>
                <span className={`font-medium ${statusClass}`}>{statusText}</span>
                {progress.onHold && <OnHoldBadge />}
                <div className="ml-auto h-2 w-40 overflow-hidden rounded bg-gray-200" role="progressbar" aria-valuemin={0} aria-valuemax={line.quantity} aria-valuenow={line.reservedQuantity}
                    aria-label={t('sales_col_progress')}>
                    <div className={`h-full ${barClass}`} style={{ width: `${line.quantity > 0 ? Math.min(100, (line.reservedQuantity / line.quantity) * 100) : 0}%` }} />
                </div>
            </header>

            {reloadFailed && (
                <p role="alert" className="mx-3 mt-2 rounded border border-red-300 bg-red-50 px-2 py-1 text-sm text-red-900">{t('sales_reload_failed')}</p>
            )}

            {lineBlock !== null && (
                <p role="status" className="mx-3 mt-2 rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_pending_blocked_line')}</p>
            )}

            {notice && (
                <div role="status" className="mx-3 mt-2 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">
                    <p className="font-medium"><span aria-hidden="true">✓ </span>{notice.kind === 'reserved' ? t('sales_notice_reserved') : t('sales_notice_released')}</p>
                    {notice.replayed && <p className="mt-1">{t('supply_saved_before')}</p>}
                    <p className="mt-1">
                        {notice.snapshot === 'failed'
                            ? t('sales_notice_availability_failed')
                            : notice.snapshot === null
                                ? t('inspection_notice_no_supply', { unit: line.unit })
                                : t('sales_notice_availability', {
                                    unit: line.unit,
                                    before: formatQuantity(notice.snapshot.availableBeforeReservations.quantity, locale),
                                    reserved: formatQuantity(notice.snapshot.reserved.quantity, locale),
                                    after: formatQuantity(notice.snapshot.availableAfterReservations.quantity, locale),
                                    afterKg: formatKg(notice.snapshot.availableAfterReservations.netWeightKg, locale),
                                })}
                    </p>
                </div>
            )}

            <div className="px-3 py-2">
                <h4 className="text-xs font-semibold uppercase tracking-wide text-gray-600">{t('sales_reservations_title')}</h4>
                {reservations.length === 0 ? (
                    <p className="py-1 text-sm text-gray-700">{t('sales_no_reservations')}</p>
                ) : (
                    <div className="mt-1 overflow-x-auto rounded border border-gray-200">
                        <table className="min-w-full divide-y divide-gray-200 text-sm">
                            <caption className="sr-only">{t('sales_reservations_title')}</caption>
                            <thead className="bg-gray-50">
                                <tr>
                                    <th scope="col" className={th}>{t('availability_col_pallet')}</th>
                                    <th scope="col" className={th}>{t('availability_col_lot')}</th>
                                    <th scope="col" className={th}>{t('sales_col_bin')}</th>
                                    <th scope="col" className={`${th} text-right`}>{t('supply_quantity')}</th>
                                    <th scope="col" className={`${th} text-right`}>{t('supply_actual_net_kg')}</th>
                                    <th scope="col" className={th}>{t('sales_col_status')}</th>
                                    <th scope="col" className={th}>{t('sales_col_content_quality')}</th>
                                    <th scope="col" className={th}>{t('sales_col_created')}</th>
                                    <th scope="col" className={th}>{t('inspection_actions')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {reservations.map((reservation) => {
                                    const pendingRelease = commands[releaseKey(order.id, line.id, reservation.id)] !== undefined;
                                    const releaseOpen = pendingRelease || releasingId === reservation.id;
                                    return (
                                        <tr key={reservation.id} className={reservation.onHold ? 'bg-red-50' : reservation.status !== 'Active' ? 'text-gray-600' : undefined}>
                                            <td className={`${td} font-mono`}>{reservation.palletReference ?? t('availability_unknown')}</td>
                                            <td className={td}>{reservation.lotNumber ?? t('availability_unknown')}</td>
                                            <td className={`${td} font-mono text-xs`}>{reservation.warehouseLocation ?? t('supply_no_bin')}</td>
                                            <td className={`${td} text-right`}>{formatQuantity(reservation.quantity, locale)} <UnitBadge unit={reservation.unit} /></td>
                                            <td className={`${td} text-right`}>{formatKg(reservation.netWeightKg, locale)}</td>
                                            <td className={td}>
                                                <ReservationStatusBadge status={reservation.status} />
                                                {reservation.onHold && <span className="mt-1 block"><OnHoldBadge /></span>}
                                                {reservation.status === 'Released' && reservation.releaseReason && (
                                                    <span className="mt-1 block text-xs italic text-gray-700">“{reservation.releaseReason}”</span>
                                                )}
                                            </td>
                                            <td className={td}>
                                                {reservation.contentQualityStatus === null
                                                    ? <span className="text-xs text-amber-900">{t('sales_quality_unknown')}</span>
                                                    : <QualityBadge status={reservation.contentQualityStatus} />}
                                                {reservation.onHold && <span className="mt-1 block text-xs text-red-900">{t('sales_on_hold_hint')}</span>}
                                            </td>
                                            <td className={`${td} text-xs`}>
                                                {formatDateTime(reservation.createdAt, locale)}
                                                {reservation.releasedAt && <span className="block">{t('sales_released_at', { date: formatDateTime(reservation.releasedAt, locale) })}</span>}
                                            </td>
                                            <td className={`${td} w-80`}>
                                                {reservation.status === 'Active' && !readOnly && !releaseOpen && (
                                                    <button type="button" className={secondaryButtonClass} onClick={() => setReleasingId(reservation.id)} disabled={lineBlock !== null}
                                                        aria-label={t('sales_release_label', { pallet: reservation.palletReference ?? '', lot: reservation.lotNumber ?? '' })}>
                                                        {t('sales_release')}
                                                    </button>
                                                )}
                                                {releaseOpen && (
                                                    <ReleaseReservationForm key={`${reservation.id}:${reservation.version}:${decisionToken}`} orderId={order.id} line={line}
                                                        reservation={reservation} actor={actor} onSucceeded={onReleased} onReload={() => void reload()} onCancel={() => setReleasingId(null)} />
                                                )}
                                            </td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            <div className="border-t border-gray-200 px-3 py-2">
                {!showChooser && (
                    <button type="button" className={primaryButtonClass} disabled={!canStartReserve} onClick={() => { setNotice(null); setChoosing(true); }}>
                        {t('sales_choose_stock')}
                    </button>
                )}
                {!showChooser && !readOnly && line.openQuantity === 0 && <span className="ml-3 text-xs text-gray-600">{t('sales_line_fully_reserved_hint')}</span>}
                {showChooser && (
                    <div className="space-y-3">
                        <div className="flex items-center justify-between">
                            <h4 className="text-sm font-semibold text-gray-900">{t('sales_stock_title')}</h4>
                            {pendingReserve === null && (
                                <button type="button" className={secondaryButtonClass} onClick={() => { setChoosing(false); setSelected(null); }}>{t('sales_close_stock')}</button>
                            )}
                        </div>
                        <p className="text-xs text-gray-600">{t('sales_stock_rules')}</p>
                        <ReservableStockTable orderId={order.id} line={line} token={stockToken} selectedId={formContentId}
                            onSelect={setSelected} onLoaded={onStockLoaded} selectable={pendingReserve === null} />
                        {formContentId !== null && (
                            <ReserveForm key={`${formContentId}:${line.version}:${decisionToken}`}
                                orderId={order.id} line={line} stock={formStock} actor={actor}
                                onSucceeded={onReserved} onReload={() => void reload()}
                                onCancel={() => (pendingReserve === null ? setSelected(null) : setChoosing(false))} />
                        )}
                    </div>
                )}
            </div>
        </section>
    );
}
