'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { formatDateOnly } from '../../lib/calendarDate';
import { CorrectDtShipmentCommand, blockedBy, correctDtShipmentKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, RuleError, normalizeActor, normalizeReason } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { correctDirectTradeShipment } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { DirectTradeShipment, DirectTradeShipmentCorrection, SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import TechnicalDetails from '../supply/TechnicalDetails';

// Terugnemen van een volledige DirectTrade-verzending die niet heeft plaatsgevonden. Toont alle betrokken regels, eist een reden en de
// standaard niet-aangevinkte verklaring; zonder die twee gaat er niets naar de server. De opdracht (orderversie en alle toewijzingen met
// hun versies) wordt bevroren en boven dit paneel bewaard; staat de verzending na verversen al als teruggenomen, dan blijft alleen de
// identieke herhaling mogelijk.
export default function DirectTradeShipmentCorrectionPanel({ order, shipment, actor, onCorrected, onReload, onClose }: {
    order: SalesOrder;
    shipment: DirectTradeShipment;
    actor: string;
    onCorrected: (saved: SaveResult<DirectTradeShipmentCorrection>) => Promise<void>;
    onReload: () => void;
    onClose: () => void;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const [pending, setPending, clearPending] = usePendingCommand<CorrectDtShipmentCommand>(correctDtShipmentKey(order.id, shipment.id));
    const [reason, setReason] = useState(() => pending?.payload.reason ?? '');
    const [confirmed, setConfirmed] = useState(() => pending !== null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const blocker = blockedBy(commands, { kind: 'correct-dt-shipment', orderId: order.id });
    const blockedByOther = blocker !== null && blocker.key !== correctDtShipmentKey(order.id, shipment.id);
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict || blockedByOther;
    const alreadyCorrected = shipment.correction !== null;
    const idPrefix = `dt-correct-${shipment.id}`;
    const reservations = order.lines.flatMap((line) => line.reservations);

    const submit = async () => {
        if (inFlight.current || blockedByOther) return;
        let command = pending;
        if (command === null) {
            if (alreadyCorrected || order.status !== 'Open') return;
            const errors: RuleError[] = [];
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, true);
            if (!actorResult.ok) errors.push(...actorResult.errors);
            if (!reasonResult.ok) errors.push(...reasonResult.errors);
            if (!confirmed) errors.push({ field: 'shipmentDidNotTakePlace', code: 'required' });
            if (errors.length > 0 || !actorResult.ok || !reasonResult.ok || reasonResult.value === null) {
                setFieldErrors(ruleErrorsToFieldErrors(errors, t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'correct-dt-shipment',
                key: correctDtShipmentKey(order.id, shipment.id),
                orderId: order.id,
                shipmentId: shipment.id,
                requestId,
                payload: {
                    requestId,
                    expectedOrderVersion: order.version,
                    actorReference: actorResult.value,
                    reason: reasonResult.value,
                    shipmentDidNotTakePlace: true,
                    lines: shipment.lines.map((line) => ({
                        stockReservationId: line.stockReservationId,
                        expectedReservationVersion: reservations.find((reservation) => reservation.id === line.stockReservationId)?.version ?? 0,
                    })),
                },
                display: { customerName: shipment.customerName, lineCount: shipment.lines.length },
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
            const saved = await correctDirectTradeShipment(order.id, shipment.id, command.payload);
            clearPending();
            await onCorrected(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as CorrectDtShipmentCommand);
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
            className="space-y-3 rounded-md border-2 border-amber-400 bg-amber-50/40 p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h3 id={`${idPrefix}-title`} className="text-base font-semibold text-amber-950"><span aria-hidden="true">↺ </span>{t('sales_dt_correct_title', { id: String(shipment.id) })}</h3>
                    <p className="text-xs text-gray-700">{t('sales_dt_correct_hint')}</p>
                </div>
                {pending === null && <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('sales_close_form')}</button>}
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {pending !== null && alreadyCorrected && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_dt_correct_replay_only')}</p>
            )}
            {blockedByOther && (
                <p role="status" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_dt_correct_blocked')}</p>
            )}

            <section aria-labelledby={`${idPrefix}-lines`}>
                <h4 id={`${idPrefix}-lines`} className="text-sm font-semibold text-gray-900">{t('sales_dt_correct_lines')}</h4>
                <ul className="mt-1 divide-y divide-gray-200 rounded border border-gray-200 bg-white text-sm">
                    {shipment.lines.map((line) => (
                        <li key={line.id} className="flex flex-wrap items-center gap-2 px-2 py-1.5">
                            <span className="text-gray-800">{line.vendorName} · {line.origin} → {line.destination ?? t('availability_unknown')}</span>
                            <span className="text-gray-700">{line.productName} · {line.productVariantName}</span>
                            <span>{line.lotNumber}</span>
                            <span className="text-xs text-gray-600">{t('sales_ship_shipped_on', { date: formatDateOnly(shipment.shippedOn, locale) })}</span>
                            <span className="ml-auto font-medium">{formatQuantity(line.quantity, locale)}</span>
                            <UnitBadge unit={line.unit} />
                            <span>{formatKg(line.netWeightKg, locale)}</span>
                        </li>
                    ))}
                </ul>
            </section>

            <div className="rounded border border-gray-300 bg-white px-3 py-2 text-xs text-gray-800">
                <p className="font-semibold">{t('sales_dt_correct_effects_title')}</p>
                <ul className="mt-1 list-disc pl-5">
                    <li>{t('sales_dt_correct_effect_history')}</li>
                    <li>{t('sales_dt_correct_effect_demand')}</li>
                    <li>{t('sales_dt_correct_effect_capacity')}</li>
                    <li>{t('sales_dt_correct_effect_no_allocation')}</li>
                    <li>{t('sales_dt_correct_effect_no_stock')}</li>
                </ul>
            </div>

            <div>
                <label htmlFor={`${idPrefix}-reason`} className="text-xs font-medium text-gray-700">{t('sales_dt_correct_reason')} *</label>
                <textarea id={`${idPrefix}-reason`} value={reason} rows={2} disabled={locked} maxLength={MAX_REASON_LENGTH}
                    onChange={(event) => { if (!locked) { setReason(event.target.value); setError(null); setFieldErrors({}); } }}
                    aria-invalid={fieldErrors.reason !== undefined} className={inputClass} />
                <FieldError id={`${idPrefix}-reason-error`} messages={fieldErrors.reason} />
                <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
            </div>

            <div className="rounded border-2 border-amber-400 bg-white px-2 py-1.5">
                <label className="flex items-start gap-2 text-sm font-medium text-gray-900">
                    <input id={`${idPrefix}-confirm`} type="checkbox" checked={confirmed} disabled={locked}
                        onChange={(event) => { if (!locked) { setConfirmed(event.target.checked); setFieldErrors({}); } }}
                        aria-invalid={fieldErrors.shipmentDidNotTakePlace !== undefined} />
                    <span>{t('sales_dt_correct_confirm_label')}</span>
                </label>
                <FieldError id={`${idPrefix}-confirm-error`} messages={fieldErrors.shipmentDidNotTakePlace} />
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

            {!locked && !alreadyCorrected && (
                <div className="flex gap-2">
                    <button type="submit" className={`${primaryButtonClass} bg-amber-700 hover:bg-amber-800`}>{t('sales_dt_correct_submit', { count: String(shipment.lines.length) })}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onClose}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
