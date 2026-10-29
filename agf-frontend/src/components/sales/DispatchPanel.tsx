'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { DispatchCommand, blockedBy, dispatchKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { dispatchTotals, isDirectTradeAllocation, isDispatchable, normalizeActor, normalizeDispatchReference } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { createSalesDispatch } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { SalesDispatch, SalesOrder, StockReservation } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { OnHoldBadge, ReservationStatusBadge } from './ReservationBadges';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import TechnicalDetails from '../supply/TechnicalDetails';

// Verzenden: hele Active reserveringen kiezen (on hold is zichtbaar maar niet kiesbaar), totalen per eenheid, optionele
// referentie. De opdracht (met de versies van order en reserveringen) wordt bevroren en boven dit paneel bewaard; ook als
// een verse orderrespons de gekozen reserveringen al als Consumed toont, blijft de identieke herhaling mogelijk (replay 200).
export default function DispatchPanel({ order, actor, onDispatched, onReload, onClose }: {
    order: SalesOrder;
    actor: string;
    onDispatched: (saved: SaveResult<SalesDispatch>) => Promise<void>;
    onReload: () => void;
    onClose: () => void;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const [pending, setPending, clearPending] = usePendingCommand<DispatchCommand>(dispatchKey(order.id));
    const [selectedIds, setSelectedIds] = useState<number[]>(() => pending?.payload.lines.map((line) => line.stockReservationId) ?? []);
    const [reference, setReference] = useState(() => pending?.payload.reference ?? '');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const blocker = blockedBy(commands, { kind: 'dispatch', orderId: order.id });
    const blockedByOther = blocker !== null && blocker.kind !== 'dispatch';
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict || blockedByOther;
    const readOnlyOrder = order.status !== 'Open';
    const idPrefix = `dispatch-${order.id}`;

    // Toewijzingen van verwachte, klantgebonden aanvoer (DirectTrade) horen hier niet thuis: die worden nooit vanuit het magazijn verzonden.
    const rows = order.lines.flatMap((line) => line.reservations
        .filter((reservation) => reservation.status !== 'Released' && !isDirectTradeAllocation(reservation))
        .map((reservation) => ({ line, reservation })));
    const excludedAllocations = order.lines.flatMap((line) => line.reservations).filter((reservation) => reservation.status === 'Active' && isDirectTradeAllocation(reservation)).length;
    const dispatchable = rows.filter(({ reservation }) => isDispatchable(reservation));
    const selected = rows.filter(({ reservation }) => selectedIds.includes(reservation.id)).map(({ reservation }) => reservation);
    const totals = dispatchTotals(selected);
    // Replay-only: er staat een onafgeronde opdracht en (een deel van) de gekozen reserveringen is al Consumed of niet meer kiesbaar.
    const pendingAlreadyApplied = pending !== null && pending.payload.lines.some((line) => {
        const reservation = rows.find((row) => row.reservation.id === line.stockReservationId)?.reservation;
        return reservation === undefined || reservation.status === 'Consumed';
    });

    const toggle = (reservation: StockReservation) => {
        if (locked || !isDispatchable(reservation)) return;
        setSelectedIds((current) => (current.includes(reservation.id) ? current.filter((id) => id !== reservation.id) : [...current, reservation.id]));
        setError(null);
        setFieldErrors({});
    };

    const selectAll = () => {
        if (locked) return;
        setSelectedIds(dispatchable.map(({ reservation }) => reservation.id));
        setError(null);
        setFieldErrors({});
    };

    const editReference = (value: string) => {
        if (locked) return;
        setReference(value);
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current || blockedByOther) return;
        let command = pending;
        if (command === null) {
            if (readOnlyOrder) return;
            const actorResult = normalizeActor(actor);
            const referenceResult = normalizeDispatchReference(reference);
            const chosen = dispatchable.filter(({ reservation }) => selectedIds.includes(reservation.id));
            const errors = [...(actorResult.ok ? [] : actorResult.errors), ...(referenceResult.ok ? [] : referenceResult.errors)];
            if (chosen.length === 0) errors.push({ field: 'lines', code: 'required' });
            if (errors.length > 0 || !actorResult.ok || !referenceResult.ok) {
                setFieldErrors(ruleErrorsToFieldErrors(errors, t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'dispatch',
                key: dispatchKey(order.id),
                orderId: order.id,
                requestId,
                payload: {
                    requestId,
                    expectedOrderVersion: order.version,
                    actorReference: actorResult.value,
                    reference: referenceResult.value,
                    lines: chosen.map(({ reservation }) => ({ stockReservationId: reservation.id, expectedReservationVersion: reservation.version })),
                },
                display: { customerName: order.customerName ?? String(order.customerId), lineCount: chosen.length },
                attempts: 0,
                lastError: null,
                frozenAt: new Date().toISOString(),
            };
            setPending(command);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const saved = await createSalesDispatch(order.id, command.payload);
            clearPending();
            await onDispatched(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as DispatchCommand);
            } else {
                clearPending();
            }
            setError(apiError);
            setFieldErrors(apiError.fieldErrors);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    return (
        <form noValidate aria-busy={submitting} aria-labelledby={`${idPrefix}-title`} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="space-y-3 rounded-md border-2 border-emerald-300 bg-emerald-50/40 p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h3 id={`${idPrefix}-title`} className="text-base font-semibold text-emerald-900">{t('sales_dispatch_title', { id: String(order.id) })}</h3>
                    <p className="text-xs text-gray-700">{t('sales_dispatch_hint')}</p>
                </div>
                {pending === null && <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('sales_close_form')}</button>}
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {pendingAlreadyApplied && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_dispatch_replay_only')}</p>
            )}
            {blockedByOther && (
                <p role="status" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_dispatch_blocked')}</p>
            )}
            {readOnlyOrder && pending === null && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_order_not_open')}</p>
            )}

            <section aria-labelledby={`${idPrefix}-select`}>
                <div className="flex items-center justify-between">
                    <h4 id={`${idPrefix}-select`} className="text-sm font-semibold text-gray-900">{t('sales_dispatch_select')}</h4>
                    <button type="button" data-tour="sales-dispatch-all" className={secondaryButtonClass} onClick={selectAll} disabled={locked || dispatchable.length === 0}>{t('sales_dispatch_select_all')}</button>
                </div>
                {excludedAllocations > 0 && (
                    <p role="note" className="mt-1 rounded border border-violet-300 bg-violet-50 px-2 py-1 text-xs text-violet-950">{t('sales_dispatch_allocations_excluded', { count: String(excludedAllocations) })}</p>
                )}
                {rows.length === 0 ? (
                    <p className="mt-1 text-sm text-gray-700">{t('sales_dispatch_nothing')}</p>
                ) : (
                    <ul className="mt-1 divide-y divide-gray-200 rounded border border-gray-200 bg-white text-sm">
                        {rows.map(({ line, reservation }) => {
                            const checked = selectedIds.includes(reservation.id);
                            const enabled = !locked && isDispatchable(reservation);
                            return (
                                <li key={reservation.id} className={`flex flex-wrap items-center gap-2 px-2 py-1.5 ${reservation.onHold ? 'bg-red-50' : reservation.status === 'Consumed' ? 'text-gray-600' : ''}`}>
                                    <input id={`${idPrefix}-r-${reservation.id}`} type="checkbox" checked={checked} disabled={!enabled} onChange={() => toggle(reservation)}
                                        aria-label={t('sales_dispatch_pick', { pallet: reservation.palletReference ?? '', lot: reservation.lotNumber ?? '' })} />
                                    <label htmlFor={`${idPrefix}-r-${reservation.id}`} className="flex flex-1 flex-wrap items-center gap-2">
                                        <span className="font-mono">{reservation.palletReference ?? t('availability_unknown')}</span>
                                        <span>{reservation.lotNumber ?? t('availability_unknown')}</span>
                                        <span className="text-gray-700">{line.productName} · {line.productVariantName}</span>
                                        <span className="ml-auto font-medium">{formatQuantity(reservation.quantity, locale)}</span>
                                        <UnitBadge unit={reservation.unit} />
                                        <span>{formatKg(reservation.netWeightKg ?? 0, locale)}</span>
                                        {reservation.status !== 'Active' && <ReservationStatusBadge status={reservation.status} />}
                                        {reservation.onHold && <OnHoldBadge />}
                                    </label>
                                </li>
                            );
                        })}
                    </ul>
                )}
                <FieldError id={`${idPrefix}-lines-error`} messages={fieldErrors.lines} />
            </section>

            <dl className="grid grid-cols-3 gap-x-4 gap-y-1 rounded border border-gray-300 bg-white px-3 py-2 text-xs text-gray-800" aria-live="polite">
                <dt className="text-gray-600">{t('sales_dispatch_totals')}</dt>
                <dd className="col-span-2 font-medium">
                    {totals.length === 0 ? t('sales_dispatch_totals_none') : totals.map((total) => (
                        <span key={total.unit} className="mr-3 inline-flex items-center gap-1">
                            {formatQuantity(total.quantity, locale)} <UnitBadge unit={total.unit as never} /> · {formatKg(total.netWeightKg, locale)} ({t('sales_dispatch_count', { count: String(total.count) })})
                        </span>
                    ))}
                </dd>
            </dl>

            <div>
                <label htmlFor={`${idPrefix}-reference`} className="text-xs font-medium text-gray-700">{t('sales_dispatch_reference')} ({t('inspection_optional')})</label>
                <input id={`${idPrefix}-reference`} value={reference} disabled={locked} maxLength={100} placeholder={t('sales_dispatch_reference_placeholder')}
                    onChange={(event) => editReference(event.target.value)} aria-invalid={fieldErrors.reference !== undefined}
                    aria-describedby={fieldErrors.reference ? `${idPrefix}-reference-error` : undefined} className={inputClass} />
                <FieldError id={`${idPrefix}-reference-error`} messages={fieldErrors.reference} />
                <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
                <FieldError id={`${idPrefix}-version-error`} messages={fieldErrors.expectedOrderVersion} />
            </div>
            <p className="text-xs text-gray-700">{t('sales_dispatch_based_on_version', { version: String(pending?.payload.expectedOrderVersion ?? order.version) })}</p>

            {error && pending === null && (
                <div role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1.5 text-xs text-red-900">
                    <p className="font-medium">{conflict ? t('sales_conflict') : t('supply_error_http_400')}</p>
                    {error.status !== 400 && <TechnicalDetails error={error} />}
                    {conflict && (
                        <div className="mt-2 flex flex-wrap gap-2">
                            <button type="button" className={secondaryButtonClass} onClick={onReload}>{t('sales_reload_and_decide')}</button>
                        </div>
                    )}
                </div>
            )}

            {!locked && !readOnlyOrder && (
                <div className="flex gap-2">
                    <button type="submit" data-tour="sales-dispatch-save" className={`${primaryButtonClass} bg-emerald-700 hover:bg-emerald-800`} disabled={selected.length === 0}>
                        {t('sales_dispatch_confirm', { count: String(selected.length) })}
                    </button>
                    <button type="button" className={secondaryButtonClass} onClick={onClose}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
