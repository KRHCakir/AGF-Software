'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { unitLabelKey } from '../../lib/units';
import { PackageUnit } from '../../types/availability';
import { formatDateOnly } from '../../lib/calendarDate';
import { isActiveConfirmation } from '../../lib/deliveryState';
import { RetractDeliveryCommand, blockedBy, retractDeliveryKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, RuleError, normalizeActor, normalizeReason } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { retractDelivery } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { DeliveryConfirmation, DeliveryRetraction, SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import TechnicalDetails from '../supply/TechnicalDetails';
import { DeliverableShipment } from './DeliveryConfirmationPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';

// Intrekken van een administratief onjuiste afleverbevestiging (magazijn of DirectTrade). Toont vooraf de bevestiging, de regels en de
// effecten; reden verplicht; de verklaring staat standaard uit met de waarschuwing (niet voor retour, schade of tekort) direct ernaast.
// Bevroren commando met herstel; staat de bevestiging na verversen al als ingetrokken, dan blijft alleen de identieke herhaling.
export default function RetractDeliveryPanel({ order, shipment, confirmation, actor, onRetracted, onReload, onClose }: {
    order: SalesOrder;
    shipment: DeliverableShipment;
    confirmation: DeliveryConfirmation;
    actor: string;
    onRetracted: (saved: SaveResult<DeliveryRetraction>) => Promise<void>;
    onReload: () => void;
    onClose: () => void;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const key = retractDeliveryKey(order.id, shipment.kind, shipment.id, confirmation.id);
    const [pending, setPending, clearPending] = usePendingCommand<RetractDeliveryCommand>(key);
    const [reason, setReason] = useState(() => pending?.payload.reason ?? '');
    const [confirmed, setConfirmed] = useState(() => pending !== null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const blocker = blockedBy(commands, { kind: 'retract-delivery', orderId: order.id });
    const blockedByOther = blocker !== null && blocker.key !== key;
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict || blockedByOther;
    const alreadyRetracted = !isActiveConfirmation(confirmation);
    const idPrefix = `retract-${shipment.kind === 'Warehouse' ? 'w' : 'dt'}-${shipment.id}`;
    const totals = Object.entries(shipment.lines.reduce<Partial<Record<PackageUnit, number>>>((sum, line) => ({ ...sum, [line.unit]: (sum[line.unit] ?? 0) + line.quantity }), {})) as [PackageUnit, number][];

    const submit = async () => {
        if (inFlight.current || blockedByOther) return;
        let command = pending;
        if (command === null) {
            if (alreadyRetracted || order.status !== 'Open') return;
            const errors: RuleError[] = [];
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, true);
            if (!actorResult.ok) errors.push(...actorResult.errors);
            if (!reasonResult.ok) errors.push(...reasonResult.errors);
            if (!confirmed) errors.push({ field: 'recordedInError', code: 'required' });
            if (errors.length > 0 || !actorResult.ok || !reasonResult.ok || reasonResult.value === null) {
                setFieldErrors(ruleErrorsToFieldErrors(errors, t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'retract-delivery',
                key,
                orderId: order.id,
                shipmentKind: shipment.kind,
                shipmentId: shipment.id,
                confirmationId: confirmation.id,
                requestId,
                payload: { requestId, expectedOrderVersion: order.version, actorReference: actorResult.value, reason: reasonResult.value, recordedInError: true },
                display: { customerName: shipment.customerName, sequence: confirmation.sequence },
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
            const saved = await retractDelivery(order.id, command.shipmentKind, command.shipmentId, command.confirmationId, command.payload);
            clearPending();
            await onRetracted(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as RetractDeliveryCommand);
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

    const title = t(shipment.kind === 'Warehouse' ? 'sales_retract_title_warehouse' : 'sales_retract_title_direct_trade', { id: String(shipment.id), sequence: String(confirmation.sequence) });

    return (
        <form noValidate aria-busy={submitting} aria-labelledby={`${idPrefix}-title`} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="space-y-3 rounded-md border-2 border-amber-400 bg-amber-50/40 p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h3 id={`${idPrefix}-title`} className="text-base font-semibold text-amber-950"><span aria-hidden="true">↺ </span>{title}</h3>
                    <p className="text-xs text-gray-700">{t('sales_retract_hint')}</p>
                </div>
                {pending === null && <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('sales_close_form')}</button>}
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {pending !== null && alreadyRetracted && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_retract_replay_only')}</p>
            )}
            {blockedByOther && (
                <p role="status" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_delivery_blocked')}</p>
            )}

            <section aria-labelledby={`${idPrefix}-confirmation`} className="rounded border border-gray-200 bg-white px-3 py-2 text-sm">
                <h4 id={`${idPrefix}-confirmation`} className="font-semibold text-gray-900">{t('sales_retract_confirmation_heading', { sequence: String(confirmation.sequence) })}</h4>
                <p className="text-xs text-gray-800">
                    {t('sales_delivery_recorded', { date: formatDateOnly(confirmation.deliveredOn, locale), actor: confirmation.actorReference, recorded: formatDateTime(confirmation.recordedAt, locale) })}
                    {confirmation.receiptReference && <> · <span className="font-mono">{confirmation.receiptReference}</span></>}
                </p>
                <ul className="mt-1 divide-y divide-gray-100">
                    {shipment.lines.map((line) => (
                        <li key={line.id} className="flex flex-wrap items-center gap-2 py-1">
                            <span className="text-gray-800">{line.label}</span>
                            {line.lot && <span>{line.lot}</span>}
                            <span className="ml-auto font-medium">{formatQuantity(line.quantity, locale)}</span>
                            <UnitBadge unit={line.unit} />
                            <span>{formatKg(line.netWeightKg, locale)}</span>
                        </li>
                    ))}
                </ul>
            </section>

            <div className="rounded border border-gray-300 bg-white px-3 py-2 text-xs text-gray-800" data-testid={`${idPrefix}-effects`}>
                <p className="font-semibold">{t('sales_retract_effects_title')}</p>
                <ul className="mt-1 list-disc pl-5">
                    <li>{t('sales_retract_effect_kept')}</li>
                    <li>{t('sales_retract_effect_status')}</li>
                    <li>{t('sales_retract_effect_delivered', { quantities: totals.map(([unit, quantity]) => `${formatQuantity(quantity, locale)} ${t(unitLabelKey(unit))}`).join(' · ') })}</li>
                    <li>{t('sales_retract_effect_no_stock')}</li>
                    <li>{t('sales_retract_effect_next')}</li>
                </ul>
            </div>

            <div>
                <label htmlFor={`${idPrefix}-reason`} className="text-xs font-medium text-gray-700">{t('sales_retract_reason')} *</label>
                <textarea id={`${idPrefix}-reason`} value={reason} rows={2} disabled={locked} maxLength={MAX_REASON_LENGTH}
                    onChange={(event) => { if (!locked) { setReason(event.target.value); setError(null); setFieldErrors({}); } }}
                    aria-invalid={fieldErrors.reason !== undefined} className={inputClass} />
                <FieldError id={`${idPrefix}-reason-error`} messages={fieldErrors.reason} />
                <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
            </div>

            <div className="rounded border-2 border-amber-400 bg-white px-2 py-1.5" data-testid={`${idPrefix}-statement`}>
                <label className="flex items-start gap-2 text-sm font-medium text-gray-900">
                    <input id={`${idPrefix}-confirm`} type="checkbox" checked={confirmed} disabled={locked} aria-describedby={`${idPrefix}-warning`}
                        onChange={(event) => { if (!locked) { setConfirmed(event.target.checked); setFieldErrors({}); } }}
                        aria-invalid={fieldErrors.recordedInError !== undefined} />
                    <span>{t('sales_retract_confirm_label')}</span>
                </label>
                <p id={`${idPrefix}-warning`} role="note" className="mt-1 pl-6 text-xs font-medium text-amber-900"><span aria-hidden="true">⚠ </span>{t('sales_retract_warning')}</p>
                <FieldError id={`${idPrefix}-confirm-error`} messages={fieldErrors.recordedInError} />
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

            {!locked && !alreadyRetracted && (
                <div className="flex gap-2">
                    <button type="submit" className={`${primaryButtonClass} bg-amber-700 hover:bg-amber-800`}>{t('sales_retract_submit')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onClose}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
