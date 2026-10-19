'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { CancelOrderCommand, blockedBy, cancelKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, normalizeActor, normalizeReason } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { cancelSalesOrder } from '../../services/salesOrderService';
import { SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { OnHoldBadge } from './ReservationBadges';
import { ruleErrorsToFieldErrors } from './ruleMessages';

// Annuleren van een Open order met een verplichte reden en de orderversie waarop de beslissing is gebaseerd.
// De bevestiging toont precies welke Active reserveringen (ook on hold) de server in dezelfde transactie vrijgeeft.
// De opdracht wordt bevroren en boven dit paneel bewaard; alleen exact dezelfde herhaling of een definitief
// antwoord (400/404/409) rondt haar af. Bij 409 wordt herladen en opnieuw beslist.
export default function CancelOrderPanel({ order, actor, onCancelled, onReload, onClose }: {
    order: SalesOrder;
    actor: string;
    onCancelled: (order: SalesOrder) => Promise<void>;
    onReload: () => void;
    onClose: () => void;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const [pending, setPending, clearPending] = usePendingCommand<CancelOrderCommand>(cancelKey(order.id));
    const [reason, setReason] = useState(() => pending?.payload.reason ?? '');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const blocker = blockedBy(commands, { kind: 'cancel-order', orderId: order.id });
    const blockedByLine = blocker !== null && blocker.kind !== 'cancel-order';
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    // Niet meer Open (bijvoorbeeld al geannuleerd door een eerder commando waarvan het antwoord verloren ging):
    // dan is alleen de identieke herhaling van een onafgerond commando nog mogelijk, nooit een nieuwe annulering.
    const replayOnly = order.status !== 'Open';
    const locked = submitting || pending !== null || conflict || blockedByLine || replayOnly;
    const basedOnVersion = pending?.payload.expectedOrderVersion ?? order.version;
    const active = order.lines.flatMap((line) => line.reservations.filter((reservation) => reservation.status === 'Active').map((reservation) => ({ line, reservation })));
    const consumed = order.lines.some((line) => line.reservations.some((reservation) => reservation.status === 'Consumed'));
    const idPrefix = `cancel-${order.id}`;

    const editReason = (value: string) => {
        if (locked) return;
        setReason(value);
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current || blockedByLine) return;
        let command = pending;
        if (command === null) {
            if (replayOnly) return;
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, true);
            if (!actorResult.ok || !reasonResult.ok || reasonResult.value === null) {
                setFieldErrors(ruleErrorsToFieldErrors([...(actorResult.ok ? [] : actorResult.errors), ...(reasonResult.ok ? [] : reasonResult.errors)], t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'cancel-order',
                key: cancelKey(order.id),
                orderId: order.id,
                requestId,
                payload: { requestId, expectedOrderVersion: order.version, actorReference: actorResult.value, reason: reasonResult.value },
                display: { customerName: order.customerName ?? String(order.customerId), activeReservationCount: active.length },
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
            const cancelled = await cancelSalesOrder(order.id, command.payload);
            clearPending();
            await onCancelled(cancelled);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as CancelOrderCommand);
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
            className="space-y-3 rounded-md border-2 border-red-300 bg-red-50/40 p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h3 id={`${idPrefix}-title`} className="text-base font-semibold text-red-900">{t('sales_cancel_title', { id: String(order.id) })}</h3>
                    <p className="text-xs text-gray-700">{t('sales_cancel_hint')}</p>
                </div>
                {pending === null && <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('sales_close_form')}</button>}
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {replayOnly && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_cancel_replay_only')}</p>
            )}
            {blockedByLine && (
                <p role="status" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_cancel_blocked')}</p>
            )}
            {consumed && (
                <p role="status" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_cancel_consumed')}</p>
            )}

            <section aria-labelledby={`${idPrefix}-releases`}>
                <h4 id={`${idPrefix}-releases`} className="text-sm font-semibold text-gray-900">
                    {active.length === 0 ? t('sales_cancel_nothing_to_release') : t('sales_cancel_will_release', { count: String(active.length) })}
                </h4>
                {active.length > 0 && (
                    <ul className="mt-1 divide-y divide-gray-200 rounded border border-gray-200 bg-white text-sm">
                        {active.map(({ line, reservation }) => (
                            <li key={reservation.id} className="flex flex-wrap items-center gap-2 px-2 py-1.5">
                                <span className="font-mono">{reservation.palletReference ?? t('availability_unknown')}</span>
                                <span>{reservation.lotNumber ?? t('availability_unknown')}</span>
                                <span className="text-gray-700">{line.productName} · {line.productVariantName}</span>
                                <span className="ml-auto font-medium">{formatQuantity(reservation.quantity, locale)}</span>
                                <UnitBadge unit={reservation.unit} />
                                <span>{formatKg(reservation.netWeightKg, locale)}</span>
                                {reservation.onHold && <OnHoldBadge />}
                            </li>
                        ))}
                    </ul>
                )}
                <p className="mt-1 text-xs text-gray-600">{t('sales_cancel_released_stay')}</p>
            </section>

            <div>
                <label htmlFor={`${idPrefix}-reason`} className="text-xs font-medium text-gray-700">{t('sales_cancel_reason')} *</label>
                <textarea id={`${idPrefix}-reason`} value={reason} rows={2} disabled={locked} maxLength={MAX_REASON_LENGTH}
                    onChange={(event) => editReason(event.target.value)} aria-invalid={fieldErrors.reason !== undefined}
                    aria-describedby={fieldErrors.reason ? `${idPrefix}-reason-error` : `${idPrefix}-reason-count`} className={inputClass} />
                <span id={`${idPrefix}-reason-count`} className="block text-right text-xs text-gray-500">{reason.trim().length}/{MAX_REASON_LENGTH}</span>
                <FieldError id={`${idPrefix}-reason-error`} messages={fieldErrors.reason} />
                <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
                <FieldError id={`${idPrefix}-version-error`} messages={fieldErrors.expectedOrderVersion} />
            </div>
            <p className="text-xs text-gray-700">{t('sales_cancel_based_on_version', { version: String(basedOnVersion) })}</p>

            {error && pending === null && (
                <div role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1.5 text-xs text-red-900">
                    <p className="font-medium">{conflict ? t('sales_conflict') : t('supply_error_http_400')}</p>
                    {error.kind === 'http' && error.status !== 400 && <p className="mt-1">{error.message}</p>}
                    {conflict && (
                        <div className="mt-2 flex flex-wrap gap-2">
                            <button type="button" className={secondaryButtonClass} onClick={onReload}>{t('sales_reload_and_decide')}</button>
                        </div>
                    )}
                </div>
            )}

            {!locked && !replayOnly && (
                <div className="flex gap-2">
                    <button type="submit" className={`${primaryButtonClass} bg-red-700 hover:bg-red-800`}>{t('sales_cancel_confirm', { count: String(active.length) })}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onClose}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
