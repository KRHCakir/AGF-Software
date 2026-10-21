'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { calculatedReservationKg } from '../../lib/weight';
import { parsePositiveDecimal } from '../../lib/decimal';
import { ReserveCommand, reserveKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { MAX_REASON_LENGTH, fullRemainderFits, normalizeActor, normalizeReason, remainingAfter, validateReservationInput } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { reserveStock } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { ReservableStockLine, SalesOrderLine, StockReservation } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import TechnicalDetails from '../supply/TechnicalDetails';

// Reserveren op precies één inhoudsregel. Hoeveelheid en netto-kg worden expliciet ingevoerd; kg wordt nooit berekend.
// "Volledige rest reserveren" kopieert beide exacte saldi, maar alleen als die in de resterende klantvraag passen.
// De opdracht wordt bevroren en boven dit formulier bewaard (zelfde requestId) tot de uitkomst bekend is; bij 409 wordt
// herladen en opnieuw beslist. `stock` is null als de inhoudsregel na een onbekende uitkomst niet meer in de lijst staat.
export default function ReserveForm({ orderId, line, stock, actor, onSucceeded, onReload, onCancel }: {
    orderId: number;
    line: SalesOrderLine;
    stock: ReservableStockLine | null;
    actor: string;
    onSucceeded: (saved: SaveResult<StockReservation>) => Promise<void>;
    onReload: () => void;
    onCancel: () => void;
}) {
    const { t, locale } = useTranslation();
    const [pending, setPending, clearPending] = usePendingCommand<ReserveCommand>(reserveKey(orderId, line.id));
    const [quantity, setQuantity] = useState(() => (pending === null ? '' : String(pending.payload.quantity)));
    const [netKg, setNetKg] = useState(() => (pending === null ? '' : String(pending.payload.netWeightKg)));
    const [reason, setReason] = useState(() => pending?.payload.reason ?? '');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict;
    const unitMismatch = stock !== null && stock.unit !== line.unit;
    const fits = stock !== null && fullRemainderFits(stock, line.openQuantity);
    const palletReference = stock?.palletReference ?? pending?.display.palletReference ?? '';
    const lotNumber = stock?.lotNumber ?? pending?.display.lotNumber ?? '';
    const idPrefix = `reserve-${line.id}-${stock?.palletContentLineId ?? pending?.payload.palletContentLineId ?? 0}`;

    const parsedQuantity = parsePositiveDecimal(quantity);
    const remaining = parsedQuantity.ok ? remainingAfter(line.openQuantity, parsedQuantity.value) : null;
    // Berekend gewicht: de kg volgt het aantal (aantal × kg per verpakking) en is niet vrij in te vullen; nooit "gewogen".
    const calculated = stock !== null && stock.weightMethod === 'Calculated' && stock.netKgPerPackage !== null ? stock.netKgPerPackage : null;
    const effectiveNetKg = calculated === null || stock === null ? netKg : parsedQuantity.ok ? String(calculatedReservationKg(parsedQuantity.value, calculated, stock.availableQuantity, stock.availableNetWeightKg)) : '';

    const edit = (setter: (value: string) => void) => (value: string) => {
        if (locked) return;
        setter(value);
        setError(null);
        setFieldErrors({});
    };

    const copyFullRemainder = () => {
        if (locked || !fits || stock === null) return;
        setQuantity(String(stock.availableQuantity));
        setNetKg(String(stock.availableNetWeightKg));
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current) return;
        let command = pending;
        if (command === null) {
            if (stock === null || unitMismatch) return;
            const amounts = validateReservationInput({ quantity, netKg: effectiveNetKg }, stock, line.openQuantity);
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason(reason, false);
            if (!amounts.ok || !actorResult.ok || !reasonResult.ok) {
                setFieldErrors(ruleErrorsToFieldErrors([
                    ...(amounts.ok ? [] : amounts.errors),
                    ...(actorResult.ok ? [] : actorResult.errors),
                    ...(reasonResult.ok ? [] : reasonResult.errors),
                ], t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'reserve',
                key: reserveKey(orderId, line.id),
                orderId,
                lineId: line.id,
                requestId,
                payload: {
                    requestId,
                    palletContentLineId: stock.palletContentLineId,
                    quantity: amounts.value.quantity,
                    unit: line.unit,
                    netWeightKg: amounts.value.netWeightKg,
                    actorReference: actorResult.value,
                    reason: reasonResult.value,
                },
                display: { palletReference: stock.palletReference, lotNumber: stock.lotNumber, productLabel: `${line.productName ?? ''} · ${line.productVariantName ?? ''}` },
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
            const saved = await reserveStock(orderId, line.id, command.payload);
            clearPending();
            await onSucceeded(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as ReserveCommand);
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
            className="space-y-3 rounded border border-blue-300 bg-blue-50/40 p-3">
            <h4 className="text-sm font-semibold text-gray-900">{t('sales_reserve_from', { pallet: palletReference, lot: lotNumber })}</h4>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('sales_close_form')}</button>
                </PendingNotice>
            )}

            <dl className="grid grid-cols-3 gap-x-4 gap-y-1 text-xs text-gray-800">
                <dt className="text-gray-600">{t('sales_available_on_content')}</dt>
                <dd className="col-span-2 font-medium">
                    {stock === null ? t('sales_pending_stock_missing') : (
                        <>
                            {formatQuantity(stock.availableQuantity, locale)} <UnitBadge unit={stock.unit} /> · {formatKg(stock.availableNetWeightKg, locale)}
                            {stock.warehouseLocation && <span className="ml-2 font-mono text-gray-600">{stock.warehouseLocation}</span>}
                        </>
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
                    <label htmlFor={`${idPrefix}-quantity-input`} className="text-xs font-medium text-gray-700">{t('sales_reserve_quantity')} *</label>
                    <input {...fieldProps(`${idPrefix}-quantity`, fieldErrors.quantity)} inputMode="decimal" value={quantity} disabled={locked}
                        onChange={(event) => edit(setQuantity)(event.target.value)} className={inputClass} />
                    <FieldError id={`${idPrefix}-quantity`} messages={fieldErrors.quantity} />
                </div>
                <div>
                    <label htmlFor={`${idPrefix}-kg-input`} className="text-xs font-medium text-gray-700">{t('sales_reserve_kg')} *</label>
                    <input {...fieldProps(`${idPrefix}-kg`, fieldErrors.netWeightKg)} inputMode="decimal" value={effectiveNetKg} disabled={locked || calculated !== null} readOnly={calculated !== null}
                        onChange={(event) => edit(setNetKg)(event.target.value)} className={inputClass} />
                    <span className="block text-xs text-gray-500">
                        {calculated === null ? t('sales_reserve_kg_hint') : t('sales_reserve_kg_calculated_hint', { kg: formatKg(calculated, locale), unit: line.unit })}
                    </span>
                    <FieldError id={`${idPrefix}-kg`} messages={fieldErrors.netWeightKg} />
                </div>
                <div className="flex flex-col justify-end">
                    <button type="button" className={secondaryButtonClass} onClick={copyFullRemainder} disabled={locked || !fits}>
                        {t('sales_reserve_full_remainder')}
                    </button>
                    <span className="mt-1 block text-xs text-gray-600">
                        {stock === null ? '' : fits
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
                    <button type="submit" className={primaryButtonClass} disabled={unitMismatch || stock === null}>{t('sales_confirm_reservation')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
