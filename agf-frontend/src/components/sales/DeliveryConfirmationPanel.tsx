'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { formatDateOnly, todayInAmsterdam } from '../../lib/calendarDate';
import { ConfirmDeliveryCommand, blockedBy, confirmDeliveryKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { RuleError, normalizeActor, normalizeDispatchReference, validateDeliveredOn } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { confirmDelivery } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { PackageUnit } from '../../types/availability';
import { DeliveryConfirmation, SalesOrder, ShipmentKind } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import TechnicalDetails from '../supply/TechnicalDetails';

// Wat het paneel van een verzending moet weten, voor beide soorten gelijk.
export interface DeliverableShipment {
    kind: ShipmentKind;
    id: number;
    // Verzenddatum als kalenderdatum in Europe/Amsterdam (ondergrens van de afleverdatum).
    shippedOn: string | null;
    customerName: string;
    delivered: boolean;
    lines: { id: number; label: string; lot: string | null; quantity: number; unit: PackageUnit; netWeightKg: number }[];
}

// Afleverbevestiging van één volledige verzending (magazijn of DirectTrade). Toont alle regels, een afleverdatum (standaard vandaag in
// Amsterdam, tussen verzenddatum en vandaag), een optionele referentie en de standaard niet-aangevinkte verklaring, met direct daaronder
// de waarschuwing over tekort, schade, gedeeltelijke ontvangst en retour. Bevroren commando met herstel; replay-only na verversen.
export default function DeliveryConfirmationPanel({ order, shipment, actor, onConfirmed, onReload, onClose, today = todayInAmsterdam() }: {
    order: SalesOrder;
    shipment: DeliverableShipment;
    actor: string;
    onConfirmed: (saved: SaveResult<DeliveryConfirmation>) => Promise<void>;
    onReload: () => void;
    onClose: () => void;
    today?: string;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const key = confirmDeliveryKey(order.id, shipment.kind, shipment.id);
    const [pending, setPending, clearPending] = usePendingCommand<ConfirmDeliveryCommand>(key);
    const [deliveredOn, setDeliveredOn] = useState(() => pending?.payload.deliveredOn ?? today);
    const [reference, setReference] = useState(() => pending?.payload.receiptReference ?? '');
    const [confirmed, setConfirmed] = useState(() => pending !== null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const blocker = blockedBy(commands, { kind: 'confirm-delivery', orderId: order.id });
    const blockedByOther = blocker !== null && blocker.key !== key;
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict || blockedByOther;
    const idPrefix = `delivery-${shipment.kind === 'Warehouse' ? 'w' : 'dt'}-${shipment.id}`;

    const clear = () => { setError(null); setFieldErrors({}); };

    const submit = async () => {
        if (inFlight.current || blockedByOther) return;
        let command = pending;
        if (command === null) {
            if (shipment.delivered || order.status !== 'Open') return;
            const errors: RuleError[] = [];
            const actorResult = normalizeActor(actor);
            const referenceResult = normalizeDispatchReference(reference);
            const dateResult = validateDeliveredOn(deliveredOn, shipment.shippedOn, today);
            if (!actorResult.ok) errors.push(...actorResult.errors);
            if (!referenceResult.ok) errors.push(...referenceResult.errors.map((e) => ({ ...e, field: 'receiptReference' })));
            if (!dateResult.ok) errors.push(...dateResult.errors);
            if (!confirmed) errors.push({ field: 'deliveredInFull', code: 'required' });
            if (errors.length > 0 || !actorResult.ok || !referenceResult.ok || !dateResult.ok) {
                setFieldErrors(ruleErrorsToFieldErrors(errors, t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'confirm-delivery',
                key,
                orderId: order.id,
                shipmentKind: shipment.kind,
                shipmentId: shipment.id,
                requestId,
                payload: { requestId, expectedOrderVersion: order.version, actorReference: actorResult.value, deliveredOn: dateResult.value, receiptReference: referenceResult.value, deliveredInFull: true },
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
            const saved = await confirmDelivery(order.id, command.shipmentKind, command.shipmentId, command.payload);
            clearPending();
            await onConfirmed(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as ConfirmDeliveryCommand);
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

    const title = shipment.kind === 'Warehouse'
        ? t('sales_delivery_title_warehouse', { id: String(shipment.id) })
        : t('sales_delivery_title_direct_trade', { id: String(shipment.id) });

    return (
        <form noValidate aria-busy={submitting} aria-labelledby={`${idPrefix}-title`} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="space-y-3 rounded-md border-2 border-emerald-400 bg-emerald-50/40 p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h3 id={`${idPrefix}-title`} className="text-base font-semibold text-emerald-950"><span aria-hidden="true">✓ </span>{title}</h3>
                    <p className="text-xs text-gray-700">{t('sales_delivery_hint')}</p>
                </div>
                {pending === null && <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('sales_close_form')}</button>}
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {pending !== null && shipment.delivered && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_delivery_replay_only')}</p>
            )}
            {blockedByOther && (
                <p role="status" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_delivery_blocked')}</p>
            )}

            <section aria-labelledby={`${idPrefix}-lines`}>
                <h4 id={`${idPrefix}-lines`} className="text-sm font-semibold text-gray-900">{t('sales_delivery_lines')}</h4>
                <ul className="mt-1 divide-y divide-gray-200 rounded border border-gray-200 bg-white text-sm">
                    {shipment.lines.map((line) => (
                        <li key={line.id} className="flex flex-wrap items-center gap-2 px-2 py-1.5">
                            <span className="text-gray-800">{line.label}</span>
                            {line.lot && <span>{line.lot}</span>}
                            <span className="ml-auto font-medium">{formatQuantity(line.quantity, locale)}</span>
                            <UnitBadge unit={line.unit} />
                            <span>{formatKg(line.netWeightKg, locale)}</span>
                        </li>
                    ))}
                </ul>
                {shipment.shippedOn && <p className="mt-1 text-xs text-gray-600">{t('sales_ship_shipped_on', { date: formatDateOnly(shipment.shippedOn, locale) })}</p>}
            </section>

            <div className="grid grid-cols-2 gap-3">
                <div>
                    <label htmlFor={`${idPrefix}-date`} className="text-xs font-medium text-gray-700">{t('sales_delivery_confirmed_date')} *</label>
                    <input id={`${idPrefix}-date`} type="date" value={deliveredOn} min={shipment.shippedOn ?? undefined} max={today} disabled={locked}
                        onChange={(event) => { if (!locked) { setDeliveredOn(event.target.value); clear(); } }} aria-invalid={fieldErrors.deliveredOn !== undefined} className={inputClass} />
                    <span className="block text-xs text-gray-500">{t('sales_delivery_date_hint')}</span>
                    <FieldError id={`${idPrefix}-date-error`} messages={fieldErrors.deliveredOn} />
                </div>
                <div>
                    <label htmlFor={`${idPrefix}-reference`} className="text-xs font-medium text-gray-700">{t('sales_delivery_reference')} ({t('inspection_optional')})</label>
                    <input id={`${idPrefix}-reference`} value={reference} maxLength={100} disabled={locked} placeholder={t('sales_delivery_reference_placeholder')}
                        onChange={(event) => { if (!locked) { setReference(event.target.value); clear(); } }} className={inputClass} />
                    <FieldError id={`${idPrefix}-reference-error`} messages={fieldErrors.receiptReference} />
                    <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
                </div>
            </div>

            <div className="rounded border-2 border-emerald-400 bg-white px-2 py-1.5" data-testid={`${idPrefix}-statement`}>
                <label className="flex items-start gap-2 text-sm font-medium text-gray-900">
                    <input id={`${idPrefix}-confirm`} type="checkbox" checked={confirmed} disabled={locked}
                        onChange={(event) => { if (!locked) { setConfirmed(event.target.checked); clear(); } }} aria-invalid={fieldErrors.deliveredInFull !== undefined}
                        aria-describedby={`${idPrefix}-warning`} />
                    <span>{t('sales_delivery_confirm_label')}</span>
                </label>
                <p id={`${idPrefix}-warning`} role="note" className="mt-1 pl-6 text-xs font-medium text-amber-900"><span aria-hidden="true">⚠ </span>{t('sales_delivery_warning')}</p>
                <FieldError id={`${idPrefix}-confirm-error`} messages={fieldErrors.deliveredInFull} />
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

            {!locked && !shipment.delivered && (
                <div className="flex gap-2">
                    <button type="submit" className={`${primaryButtonClass} bg-emerald-700 hover:bg-emerald-800`}>{t('sales_delivery_submit')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onClose}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
