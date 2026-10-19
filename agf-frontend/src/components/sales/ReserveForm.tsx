'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { parsePositiveDecimal } from '../../lib/decimal';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, fullRemainderFits, normalizeActor, normalizeReason, remainingAfter, validateReservationInput } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { reserveStock } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { CreateStockReservationRequest, ReservableStockLine, SalesOrderLine, StockReservation } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { ruleErrorsToFieldErrors } from './ruleMessages';

// Reserveren op precies één inhoudsregel. Hoeveelheid en netto-kg worden expliciet ingevoerd; kg wordt nooit berekend.
// "Volledige rest reserveren" kopieert beide exacte saldi, maar alleen als die in de resterende klantvraag passen.
// De opdracht wordt bevroren (zelfde requestId) voor een retry bij onbekende uitkomst; bij 409 wordt herladen en opnieuw beslist.
export default function ReserveForm({ orderId, line, stock, actor, onSucceeded, onReload, onCancel }: {
    orderId: number;
    line: SalesOrderLine;
    stock: ReservableStockLine;
    actor: string;
    onSucceeded: (saved: SaveResult<StockReservation>) => Promise<void>;
    onReload: () => void;
    onCancel: () => void;
}) {
    const { t, locale } = useTranslation();
    const [quantity, setQuantity] = useState('');
    const [netKg, setNetKg] = useState('');
    const [reason, setReason] = useState('');
    const [pending, setPending] = useState<CreateStockReservationRequest | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const unknown = error !== null && outcomeUnknown(error);
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || unknown || conflict;
    const unitMismatch = stock.unit !== line.unit;
    const fits = fullRemainderFits(stock, line.openQuantity);
    const idPrefix = `reserve-${line.id}-${stock.palletContentLineId}`;

    const parsedQuantity = parsePositiveDecimal(quantity);
    const remaining = parsedQuantity.ok ? remainingAfter(line.openQuantity, parsedQuantity.value) : null;

    const edit = (setter: (value: string) => void) => (value: string) => {
        if (locked) return;
        setter(value);
        setPending(null);
        setError(null);
        setFieldErrors({});
    };

    const copyFullRemainder = () => {
        if (locked || !fits) return;
        setQuantity(String(stock.availableQuantity));
        setNetKg(String(stock.availableNetWeightKg));
        setPending(null);
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current || unitMismatch) return;
        let request = pending;
        if (request === null) {
            const amounts = validateReservationInput({ quantity, netKg }, stock, line.openQuantity);
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, false);
            const errors = [
                ...(amounts.ok ? [] : amounts.errors),
                ...(actorResult.ok ? [] : actorResult.errors),
                ...(reasonResult.ok ? [] : reasonResult.errors),
            ];
            if (!amounts.ok || !actorResult.ok || !reasonResult.ok) {
                setFieldErrors(ruleErrorsToFieldErrors(errors, t));
                return;
            }
            setFieldErrors({});
            request = {
                requestId: newRequestId(),
                palletContentLineId: stock.palletContentLineId,
                quantity: amounts.value.quantity,
                unit: line.unit,
                netWeightKg: amounts.value.netWeightKg,
                actorReference: actorResult.value,
                reason: reasonResult.value,
            };
            setPending(request);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const saved = await reserveStock(orderId, line.id, request);
            await onSucceeded(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            setError(apiError);
            setFieldErrors(apiError.fieldErrors);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    return (
        <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="space-y-3 rounded border border-blue-300 bg-blue-50/40 p-3">
            <h4 className="text-sm font-semibold text-gray-900">
                {t('sales_reserve_from', { pallet: stock.palletReference, lot: stock.lotNumber })}
            </h4>
            <dl className="grid grid-cols-3 gap-x-4 gap-y-1 text-xs text-gray-800">
                <dt className="text-gray-600">{t('sales_available_on_content')}</dt>
                <dd className="col-span-2 font-medium">
                    {formatQuantity(stock.availableQuantity, locale)} <UnitBadge unit={stock.unit} /> · {formatKg(stock.availableNetWeightKg, locale)}
                    {stock.warehouseLocation && <span className="ml-2 font-mono text-gray-600">{stock.warehouseLocation}</span>}
                </dd>
                <dt className="text-gray-600">{t('sales_open_demand')}</dt>
                <dd className="col-span-2 font-medium">{formatQuantity(line.openQuantity, locale)} <UnitBadge unit={line.unit} /></dd>
            </dl>

            {unitMismatch && (
                <p role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1 text-xs text-red-900">{t('sales_error_unit_mismatch')}</p>
            )}

            <div className="grid grid-cols-3 gap-3">
                <div>
                    <label htmlFor={`${idPrefix}-quantity-input`} className="text-xs font-medium text-gray-700">{t('sales_reserve_quantity')} *</label>
                    <input {...fieldProps(`${idPrefix}-quantity`, fieldErrors.quantity)} inputMode="decimal" value={quantity} disabled={locked}
                        onChange={(event) => edit(setQuantity)(event.target.value)} className={inputClass} />
                    <FieldError id={`${idPrefix}-quantity`} messages={fieldErrors.quantity} />
                </div>
                <div>
                    <label htmlFor={`${idPrefix}-kg-input`} className="text-xs font-medium text-gray-700">{t('sales_reserve_kg')} *</label>
                    <input {...fieldProps(`${idPrefix}-kg`, fieldErrors.netWeightKg)} inputMode="decimal" value={netKg} disabled={locked}
                        onChange={(event) => edit(setNetKg)(event.target.value)} className={inputClass} />
                    <span className="block text-xs text-gray-500">{t('sales_reserve_kg_hint')}</span>
                    <FieldError id={`${idPrefix}-kg`} messages={fieldErrors.netWeightKg} />
                </div>
                <div className="flex flex-col justify-end">
                    <button type="button" className={secondaryButtonClass} onClick={copyFullRemainder} disabled={locked || !fits}>
                        {t('sales_reserve_full_remainder')}
                    </button>
                    <span className="mt-1 block text-xs text-gray-600">
                        {fits
                            ? t('sales_full_remainder_hint', { quantity: formatQuantity(stock.availableQuantity, locale), kg: formatKg(stock.availableNetWeightKg, locale) })
                            : t('sales_full_remainder_not_fit', { available: formatQuantity(stock.availableQuantity, locale), open: formatQuantity(line.openQuantity, locale) })}
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
                <FieldError id={`${idPrefix}-content-error`} messages={fieldErrors.palletContentLineId} />
                <FieldError id={`${idPrefix}-unit-error`} messages={fieldErrors.unit} />
            </div>

            <p className="rounded border border-gray-300 bg-white px-2 py-1.5 text-xs text-gray-900" aria-live="polite">
                {remaining === null
                    ? t('sales_remaining_demand_pending')
                    : t('sales_remaining_demand_after', { remaining: formatQuantity(remaining, locale), unit: line.unit })}
            </p>

            {error && (
                <div role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1.5 text-xs text-red-900">
                    <p className="font-medium">
                        {conflict ? t('sales_conflict') : unknown ? t('supply_error_outcome_unknown') : t('supply_error_http_400')}
                    </p>
                    {error.kind === 'http' && error.status !== 400 && <p className="mt-1">{error.message}</p>}
                    <div className="mt-2 flex flex-wrap gap-2">
                        {unknown && <button type="submit" className={primaryButtonClass} disabled={submitting}>{t('supply_retry_same')}</button>}
                        {(conflict || unknown) && <button type="button" className={secondaryButtonClass} onClick={onReload}>{t('sales_reload_and_decide')}</button>}
                    </div>
                </div>
            )}

            {!locked && (
                <div className="flex gap-2">
                    <button type="submit" className={primaryButtonClass} disabled={unitMismatch}>{t('sales_confirm_reservation')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
