'use client';

import Link from 'next/link';
import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { CorrectDispatchCommand, blockedBy, correctDispatchKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, normalizeActor, normalizeReason } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { correctSalesDispatch } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { SalesDispatch, SalesDispatchCorrection, SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import TechnicalDetails from '../supply/TechnicalDetails';

// Administratieve correctie van een hele verzending. De gebruiker bevestigt expliciet (standaard níét aangevinkt) dat de
// goederen het magazijn niet hebben verlaten; de software stelt dat niet vast. De oorspronkelijke verzending blijft bewaard;
// per regel komt een nieuwe inhoudsregel in quarantaine op dezelfde pallet, de reservering wordt Corrected en de klantvraag
// komt weer open. Er wordt geen reservering hersteld en de pallet verandert niet van status.
export default function CorrectDispatchPanel({ order, dispatch, actor, onCorrected, onReload, onClose }: {
    order: SalesOrder;
    dispatch: SalesDispatch;
    actor: string;
    onCorrected: (saved: SaveResult<SalesDispatchCorrection>) => Promise<void>;
    onReload: () => void;
    onClose: () => void;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const [pending, setPending, clearPending] = usePendingCommand<CorrectDispatchCommand>(correctDispatchKey(order.id, dispatch.id));
    const [reason, setReason] = useState(() => pending?.payload.reason ?? '');
    const [confirmed, setConfirmed] = useState(() => pending !== null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const blocker = blockedBy(commands, { kind: 'correct-dispatch', orderId: order.id });
    const blockedByOther = blocker !== null && blocker.kind !== 'correct-dispatch';
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const reservations = new Map(order.lines.flatMap((line) => line.reservations.map((reservation) => [reservation.id, reservation] as const)));
    // Replay-only: de verzending is volgens de server al gecorrigeerd (bijvoorbeeld verwerkt terwijl het antwoord verloren ging).
    const alreadyCorrected = dispatch.correction !== null || dispatch.lines.some((line) => reservations.get(line.stockReservationId)?.status === 'Corrected');
    const readOnlyOrder = order.status !== 'Open';
    const locked = submitting || pending !== null || conflict || blockedByOther || alreadyCorrected || readOnlyOrder;
    const idPrefix = `correct-${dispatch.id}`;
    const palletIds = Array.from(new Set(dispatch.lines.map((line) => line.palletId)));

    const edit = <T,>(setter: (value: T) => void) => (value: T) => {
        if (locked) return;
        setter(value);
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current || blockedByOther) return;
        let command = pending;
        if (command === null) {
            if (alreadyCorrected || readOnlyOrder) return;
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, true);
            const errors = [...(actorResult.ok ? [] : actorResult.errors), ...(reasonResult.ok ? [] : reasonResult.errors)];
            if (!confirmed) errors.push({ field: 'goodsNeverLeftWarehouse', code: 'required' });
            if (errors.length > 0 || !actorResult.ok || !reasonResult.ok || reasonResult.value === null) {
                setFieldErrors(ruleErrorsToFieldErrors(errors, t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'correct-dispatch',
                key: correctDispatchKey(order.id, dispatch.id),
                orderId: order.id,
                dispatchId: dispatch.id,
                requestId,
                payload: {
                    requestId,
                    expectedOrderVersion: order.version,
                    actorReference: actorResult.value,
                    reason: reasonResult.value,
                    goodsNeverLeftWarehouse: true,
                    lines: dispatch.lines.map((line) => ({ stockReservationId: line.stockReservationId, expectedReservationVersion: reservations.get(line.stockReservationId)?.version ?? 0 })),
                },
                display: { customerName: order.customerName ?? String(order.customerId), lineCount: dispatch.lines.length },
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
            const saved = await correctSalesDispatch(order.id, dispatch.id, command.payload);
            clearPending();
            await onCorrected(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as CorrectDispatchCommand);
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
            className="space-y-3 rounded-md border-2 border-amber-300 bg-amber-50/40 p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h3 id={`${idPrefix}-title`} className="text-base font-semibold text-amber-950">{t('sales_correct_title', { id: String(dispatch.id) })}</h3>
                    <p className="text-xs text-gray-700">{t('sales_correct_hint')}</p>
                </div>
                {pending === null && <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('sales_close_form')}</button>}
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {alreadyCorrected && pending === null && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_correct_already')}</p>
            )}
            {alreadyCorrected && pending !== null && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_correct_replay_only')}</p>
            )}
            {blockedByOther && (
                <p role="status" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_dispatch_blocked')}</p>
            )}

            <section aria-labelledby={`${idPrefix}-lines`}>
                <h4 id={`${idPrefix}-lines`} className="text-sm font-semibold text-gray-900">{t('sales_correct_lines', { count: String(dispatch.lines.length) })}</h4>
                <ul className="mt-1 divide-y divide-gray-200 rounded border border-gray-200 bg-white text-sm">
                    {dispatch.lines.map((line) => (
                        <li key={line.id} className="flex flex-wrap items-center gap-2 px-2 py-1.5">
                            <span className="font-mono">{line.palletReference ?? t('availability_unknown')}</span>
                            <span>{line.lotNumber ?? t('availability_unknown')}</span>
                            <span className="text-gray-700">{line.productName} · {line.productVariantName}</span>
                            <span className="ml-auto font-medium">{formatQuantity(line.quantity, locale)}</span>
                            <UnitBadge unit={line.unit} />
                            <span>{formatKg(line.netWeightKg, locale)}</span>
                        </li>
                    ))}
                </ul>
                <ul className="mt-2 list-disc space-y-0.5 pl-5 text-xs text-gray-800">
                    <li>{t('sales_correct_effect_quarantine')}</li>
                    <li>{t('sales_correct_effect_order')}</li>
                    <li>{t('sales_correct_effect_pallet')}</li>
                    <li>{t('sales_correct_effect_history')}</li>
                    <li className="font-medium text-amber-950">{t('sales_correct_not_a_return')}</li>
                </ul>
            </section>

            <div className="rounded border border-amber-400 bg-white px-3 py-2">
                <label className="flex items-start gap-2 text-sm text-gray-900">
                    <input id={`${idPrefix}-confirm`} type="checkbox" checked={confirmed} disabled={locked} onChange={(event) => edit(setConfirmed)(event.target.checked)}
                        aria-invalid={fieldErrors.goodsNeverLeftWarehouse !== undefined} aria-describedby={`${idPrefix}-confirm-hint`} className="mt-0.5" />
                    <span>
                        <span className="font-medium">{t('sales_correct_confirm_label')}</span>
                        <span id={`${idPrefix}-confirm-hint`} className="block text-xs text-gray-600">{t('sales_correct_confirm_hint')}</span>
                    </span>
                </label>
                <FieldError id={`${idPrefix}-confirm-error`} messages={fieldErrors.goodsNeverLeftWarehouse} />
            </div>

            <div>
                <label htmlFor={`${idPrefix}-reason`} className="text-xs font-medium text-gray-700">{t('sales_correct_reason')} *</label>
                <textarea id={`${idPrefix}-reason`} value={reason} rows={2} disabled={locked} maxLength={MAX_REASON_LENGTH}
                    onChange={(event) => edit(setReason)(event.target.value)} aria-invalid={fieldErrors.reason !== undefined}
                    aria-describedby={fieldErrors.reason ? `${idPrefix}-reason-error` : `${idPrefix}-reason-count`} className={inputClass} />
                <span id={`${idPrefix}-reason-count`} className="block text-right text-xs text-gray-500">{reason.trim().length}/{MAX_REASON_LENGTH}</span>
                <FieldError id={`${idPrefix}-reason-error`} messages={fieldErrors.reason} />
                <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
                <FieldError id={`${idPrefix}-version-error`} messages={fieldErrors.expectedOrderVersion} />
            </div>
            <p className="text-xs text-gray-700">{t('sales_correct_based_on_version', { version: String(pending?.payload.expectedOrderVersion ?? order.version) })}</p>

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

            {!locked && (
                <div className="flex flex-wrap items-center gap-2">
                    <button type="submit" className={`${primaryButtonClass} bg-amber-700 hover:bg-amber-800`}>{t('sales_correct_confirm_button', { count: String(dispatch.lines.length) })}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onClose}>{t('inspection_cancel')}</button>
                    {palletIds.map((palletId) => (
                        <Link key={palletId} href={`/inspection?palletId=${palletId}`} className="text-xs font-medium text-blue-800 underline underline-offset-2">
                            {t('sales_correct_inspection_link', { pallet: String(palletId) })}
                        </Link>
                    ))}
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
