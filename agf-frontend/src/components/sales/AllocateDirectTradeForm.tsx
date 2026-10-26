'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { formatDecimalInput, parsePositiveDecimal } from '../../lib/decimal';
import { AllocateDirectTradeCommand, allocateKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, expectedAfterRequested, fullAllocationRemainder, normalizeActor, normalizeReason, remainingAfter, validateAllocationInput } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { allocateDirectTrade } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { DirectTradeSupplyLine, SalesOrder, SalesOrderLine, StockReservation } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand } from './PendingCommandsContext';
import { sourceLabel } from './DirectTradeSupplyPicker';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import TechnicalDetails from '../supply/TechnicalDetails';

// Toewijzen van verwachte, klantgebonden DirectTrade-aanvoer aan een orderregel: alleen een hoeveelheid (geen kg: er is niets gemeten),
// een zichtbare waarschuwing met de verwachte leverdatum en een expliciete, standaard niet-aangevinkte bevestiging. Zonder
// bevestiging gaat er niets naar de server (die weigert dan ook met 400). De opdracht wordt bevroren en boven dit formulier
// bewaard (zelfde requestId) tot de uitkomst bekend is; bij 409 wordt herladen en opnieuw beslist.
export default function AllocateDirectTradeForm({ order, line, supply, actor, onSucceeded, onReload, onCancel }: {
    order: SalesOrder;
    line: SalesOrderLine;
    supply: DirectTradeSupplyLine | null;
    actor: string;
    onSucceeded: (saved: SaveResult<StockReservation>) => Promise<void>;
    onReload: () => void;
    onCancel: () => void;
}) {
    const { t, locale } = useTranslation();
    const [pending, setPending, clearPending] = usePendingCommand<AllocateDirectTradeCommand>(allocateKey(order.id, line.id));
    const [quantity, setQuantity] = useState(() => (pending === null ? '' : formatDecimalInput(pending.payload.quantity)));
    const [reason, setReason] = useState(() => pending?.payload.reason ?? '');
    const [acknowledged, setAcknowledged] = useState(() => pending !== null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict;
    const unitMismatch = supply !== null && supply.unit !== line.unit;
    const label = supply !== null ? sourceLabel(supply, t('availability_unknown')) : pending?.display.sourceLabel ?? '';
    const lotNumber = supply?.lotNumber ?? pending?.display.lotNumber ?? '';
    // Live uit de picker; zonder bronrij (verwerkt maar antwoord verloren, rest nu 0) de datum zoals getoond bij het verzenden.
    const expectedFromSnapshot = supply === null && pending !== null;
    const expectedAt = supply !== null ? supply.expectedAt : pending?.display.expectedAt ?? null;
    const late = expectedAfterRequested(expectedAt, order.requestedDeliveryDate);
    const fullRemainder = supply === null ? 0 : fullAllocationRemainder(supply.remainingQuantity, line.openQuantity);
    const idPrefix = `allocate-${line.id}-${supply?.supplyCommitmentLineId ?? pending?.payload.supplyCommitmentLineId ?? 0}`;

    const parsedQuantity = parsePositiveDecimal(quantity);
    const remaining = parsedQuantity.ok ? remainingAfter(line.openQuantity, parsedQuantity.value) : null;

    const edit = (setter: (value: string) => void) => (value: string) => {
        if (locked) return;
        setter(value);
        setError(null);
        setFieldErrors({});
    };

    const copyFullRemainder = () => {
        if (locked || fullRemainder <= 0) return;
        setQuantity(formatDecimalInput(fullRemainder));
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current) return;
        let command = pending;
        if (command === null) {
            if (supply === null || unitMismatch) return;
            const amounts = validateAllocationInput(quantity, supply.remainingQuantity, line.openQuantity);
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, false);
            const errors = [
                ...(amounts.ok ? [] : amounts.errors),
                ...(actorResult.ok ? [] : actorResult.errors),
                ...(reasonResult.ok ? [] : reasonResult.errors),
            ];
            if (!acknowledged) errors.push({ field: 'acknowledgeExpectedSupply', code: 'required' });
            if (errors.length > 0 || !amounts.ok || !actorResult.ok || !reasonResult.ok) {
                setFieldErrors(ruleErrorsToFieldErrors(errors, t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'allocate-direct-trade',
                key: allocateKey(order.id, line.id),
                orderId: order.id,
                lineId: line.id,
                requestId,
                payload: {
                    requestId,
                    supplyCommitmentLineId: supply.supplyCommitmentLineId,
                    quantity: amounts.value.quantity,
                    unit: line.unit,
                    acknowledgeExpectedSupply: true,
                    actorReference: actorResult.value,
                    reason: reasonResult.value,
                },
                display: { sourceLabel: label, lotNumber, productLabel: `${line.productName ?? ''} · ${line.productVariantName ?? ''}`, expectedAt },
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
            const saved = await allocateDirectTrade(order.id, line.id, command.payload);
            clearPending();
            await onSucceeded(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as AllocateDirectTradeCommand);
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
        <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="space-y-3 rounded border-2 border-violet-300 bg-violet-50/40 p-3">
            <h4 className="text-sm font-semibold text-gray-900">{t('sales_allocate_from', { source: label, lot: lotNumber || t('availability_unknown') })}</h4>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('sales_close_form')}</button>
                </PendingNotice>
            )}

            <div role="alert" className="rounded border-2 border-amber-400 bg-amber-50 px-3 py-2 text-sm text-amber-950">
                <p className="font-semibold"><span aria-hidden="true">⚠ </span>{t('sales_allocate_warning_title')}</p>
                <p className="mt-1">{t('sales_allocate_warning', { date: expectedAt ? formatCalendarDate(expectedAt, locale) : t('availability_unknown') })}</p>
                <p className="mt-1 font-medium" data-expected-source={expectedFromSnapshot ? 'snapshot' : 'live'}>
                    {t('sales_allocate_expected_date')}:{' '}
                    {expectedAt ? <time dateTime={expectedAt}>{formatCalendarDate(expectedAt, locale)}</time> : t('availability_unknown')}
                    {expectedFromSnapshot && <span className="ml-1 text-xs font-normal">({t('sales_allocate_expected_date_snapshot')})</span>}
                </p>
                {late && expectedAt && (
                    <p className="mt-1 font-medium">{t('sales_allocate_date_warning', { expected: formatCalendarDate(expectedAt, locale), requested: formatCalendarDate(order.requestedDeliveryDate, locale) })}</p>
                )}
            </div>

            <dl className="grid grid-cols-3 gap-x-4 gap-y-1 text-xs text-gray-800">
                <dt className="text-gray-600">{t('sales_allocate_remaining_on_source')}</dt>
                <dd className="col-span-2 font-medium">
                    {supply === null ? t('sales_pending_supply_missing') : (
                        <>{formatQuantity(supply.remainingQuantity, locale)} <UnitBadge unit={supply.unit} /> <span className="ml-2 text-gray-600">{t('sales_allocation_no_kg')}</span></>
                    )}
                </dd>
                <dt className="text-gray-600">{t('sales_open_demand')}</dt>
                <dd className="col-span-2 font-medium">{formatQuantity(line.openQuantity, locale)} <UnitBadge unit={line.unit} /></dd>
            </dl>

            {unitMismatch && (
                <p role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1 text-xs text-red-900">{t('sales_error_unit_mismatch')}</p>
            )}

            <div className="grid grid-cols-3 gap-3">
                <div>
                    <label htmlFor={`${idPrefix}-quantity-input`} className="text-xs font-medium text-gray-700">{t('sales_allocate_quantity')} *</label>
                    <input {...fieldProps(`${idPrefix}-quantity`, fieldErrors.quantity)} inputMode="decimal" value={quantity} disabled={locked}
                        onChange={(event) => edit(setQuantity)(event.target.value)} className={inputClass} />
                    <FieldError id={`${idPrefix}-quantity`} messages={fieldErrors.quantity} />
                </div>
                <div className="col-span-2 flex flex-col justify-end">
                    <button type="button" className={secondaryButtonClass} onClick={copyFullRemainder} disabled={locked || fullRemainder <= 0}>
                        {t('sales_allocate_full_remainder')}
                    </button>
                    <span className="mt-1 block text-xs text-gray-600">
                        {supply === null ? '' : t('sales_allocate_full_remainder_hint', { quantity: formatQuantity(fullRemainder, locale), unit: line.unit })}
                    </span>
                </div>
            </div>

            <div>
                <label htmlFor={`${idPrefix}-reason`} className="text-xs font-medium text-gray-700">{t('inspection_reason')} ({t('inspection_optional')})</label>
                <input id={`${idPrefix}-reason`} value={reason} disabled={locked} maxLength={MAX_REASON_LENGTH}
                    onChange={(event) => edit(setReason)(event.target.value)} aria-invalid={fieldErrors.reason !== undefined}
                    aria-describedby={fieldErrors.reason ? `${idPrefix}-reason-error` : undefined} className={inputClass} />
                <FieldError id={`${idPrefix}-reason-error`} messages={fieldErrors.reason} />
                <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
                <FieldError id={`${idPrefix}-source-error`} messages={fieldErrors.supplyCommitmentLineId} />
                <FieldError id={`${idPrefix}-unit-error`} messages={fieldErrors.unit} />
            </div>

            <div className="rounded border border-amber-300 bg-white px-2 py-1.5">
                <label className="flex items-start gap-2 text-sm text-gray-900">
                    <input id={`${idPrefix}-acknowledge`} type="checkbox" checked={acknowledged} disabled={locked}
                        onChange={(event) => { if (!locked) { setAcknowledged(event.target.checked); setFieldErrors({}); } }}
                        aria-invalid={fieldErrors.acknowledgeExpectedSupply !== undefined}
                        aria-describedby={fieldErrors.acknowledgeExpectedSupply ? `${idPrefix}-acknowledge-error` : undefined} />
                    <span>{t('sales_allocate_acknowledge')}</span>
                </label>
                <FieldError id={`${idPrefix}-acknowledge-error`} messages={fieldErrors.acknowledgeExpectedSupply} />
            </div>

            <p className="rounded border border-gray-300 bg-white px-2 py-1.5 text-xs text-gray-900" aria-live="polite">
                {remaining === null
                    ? t('sales_remaining_demand_pending')
                    : t('sales_remaining_demand_after_allocation', { remaining: formatQuantity(remaining, locale), unit: line.unit })}
            </p>

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
                <div className="flex gap-2">
                    <button type="submit" className={`${primaryButtonClass} bg-violet-700 hover:bg-violet-800`} disabled={unitMismatch || supply === null}>{t('sales_confirm_allocation')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
