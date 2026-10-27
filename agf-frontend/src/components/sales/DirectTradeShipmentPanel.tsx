'use client';

import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate, todayInAmsterdam } from '../../lib/calendarDate';
import { formatDecimalInput } from '../../lib/decimal';
import { ShipDirectTradeCommand, blockedBy, shipDirectTradeKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { RuleError, isShippableAllocation, isShippedAllocation, lotDeviates, normalizeActor, normalizeDispatchReference, validateShipmentLine, validateShippedOn } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { createDirectTradeShipment } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { DirectTradeShipment, SalesOrder, StockReservation } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { sourceLabel } from './DirectTradeSupplyPicker';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { DirectTradeShippedBadge, ExpectedAllocationBadge } from './ReservationBadges';
import { ruleErrorsToFieldErrors } from './ruleMessages';
import TechnicalDetails from '../supply/TechnicalDetails';

interface RowInput { netKg: string; lotNumber: string; lotDeviationReason: string }

// DirectTrade-verzending: hele Active toewijzingen kiezen en per regel de werkelijk verzonden netto kg (leeg, verplicht, nooit afgeleid)
// en partij vastleggen; één verzenddatum (kalenderdatum, niet na vandaag in Europe/Amsterdam). Hoeveelheid en eenheid zijn die van de
// toewijzing (alleen hele toewijzingen). De opdracht wordt bevroren en boven dit paneel bewaard; ook als een verse orderrespons de
// toewijzingen al als verzonden toont, blijft alleen de identieke herhaling mogelijk. Magazijnvoorraad verandert niet.
export default function DirectTradeShipmentPanel({ order, actor, onShipped, onReload, onClose, today = todayInAmsterdam() }: {
    order: SalesOrder;
    actor: string;
    onShipped: (saved: SaveResult<DirectTradeShipment>) => Promise<void>;
    onReload: () => void;
    onClose: () => void;
    today?: string;
}) {
    const { t, locale } = useTranslation();
    const { commands } = usePendingCommands();
    const [pending, setPending, clearPending] = usePendingCommand<ShipDirectTradeCommand>(shipDirectTradeKey(order.id));
    const [selectedIds, setSelectedIds] = useState<number[]>(() => pending?.payload.lines.map((line) => line.stockReservationId) ?? []);
    const [inputs, setInputs] = useState<Record<number, RowInput>>(() => Object.fromEntries((pending?.payload.lines ?? []).map((line) => [line.stockReservationId, {
        netKg: formatDecimalInput(line.netWeightKg), lotNumber: line.lotNumber, lotDeviationReason: line.lotDeviationReason ?? '',
    }])));
    const [shippedOn, setShippedOn] = useState(() => pending?.payload.shippedOn ?? today);
    const [reference, setReference] = useState(() => pending?.payload.reference ?? '');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);

    const blocker = blockedBy(commands, { kind: 'ship-direct-trade', orderId: order.id });
    const blockedByOther = blocker !== null && blocker.kind !== 'ship-direct-trade';
    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict || blockedByOther;
    const readOnlyOrder = order.status !== 'Open';
    const idPrefix = `dt-ship-${order.id}`;

    const rows = order.lines.flatMap((line) => line.reservations
        .filter((reservation) => isShippableAllocation(reservation) || isShippedAllocation(reservation))
        .map((reservation) => ({ line, reservation })));
    const shippable = rows.filter(({ reservation }) => isShippableAllocation(reservation));
    // Volgorde van de regels in de payload = volgorde van de rijen; foutsleutels lines[i] volgen dezelfde index.
    const chosen = shippable.filter(({ reservation }) => selectedIds.includes(reservation.id));
    const payloadIndex = (reservationId: number) => (pending ?? null) !== null
        ? pending!.payload.lines.findIndex((line) => line.stockReservationId === reservationId)
        : chosen.findIndex(({ reservation }) => reservation.id === reservationId);
    const pendingAlreadyApplied = pending !== null && pending.payload.lines.some((line) => {
        const reservation = rows.find((row) => row.reservation.id === line.stockReservationId)?.reservation;
        return reservation === undefined || reservation.status === 'Consumed';
    });

    const inputOf = (reservation: StockReservation): RowInput => inputs[reservation.id] ?? { netKg: '', lotNumber: reservation.lotNumber ?? '', lotDeviationReason: '' };

    const clearFeedback = () => { setError(null); setFieldErrors({}); };

    const toggle = (reservation: StockReservation) => {
        if (locked || !isShippableAllocation(reservation)) return;
        setSelectedIds((current) => (current.includes(reservation.id) ? current.filter((id) => id !== reservation.id) : [...current, reservation.id]));
        setInputs((current) => (current[reservation.id] ? current : { ...current, [reservation.id]: inputOf(reservation) }));
        clearFeedback();
    };

    const editRow = (reservation: StockReservation, field: keyof RowInput, value: string) => {
        if (locked) return;
        setInputs((current) => ({ ...current, [reservation.id]: { ...inputOf(reservation), ...current[reservation.id], [field]: value } }));
        clearFeedback();
    };

    const submit = async () => {
        if (inFlight.current || blockedByOther) return;
        let command = pending;
        if (command === null) {
            if (readOnlyOrder) return;
            const errors: RuleError[] = [];
            const actorResult = normalizeActor(actor);
            const referenceResult = normalizeDispatchReference(reference);
            const dateResult = validateShippedOn(shippedOn, today);
            if (!actorResult.ok) errors.push(...actorResult.errors);
            if (!referenceResult.ok) errors.push(...referenceResult.errors);
            if (!dateResult.ok) errors.push(...dateResult.errors);
            if (chosen.length === 0) errors.push({ field: 'lines', code: 'required' });
            const lines = chosen.map(({ reservation }, index) => ({ reservation, result: validateShipmentLine(index, inputOf(reservation), reservation.lotNumber) }));
            for (const { result } of lines) if (!result.ok) errors.push(...result.errors);
            if (errors.length > 0 || !actorResult.ok || !referenceResult.ok || !dateResult.ok) {
                setFieldErrors(ruleErrorsToFieldErrors(errors, t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'ship-direct-trade',
                key: shipDirectTradeKey(order.id),
                orderId: order.id,
                requestId,
                payload: {
                    requestId,
                    expectedOrderVersion: order.version,
                    actorReference: actorResult.value,
                    shippedOn: dateResult.value,
                    reference: referenceResult.value,
                    lines: lines.map(({ reservation, result }) => {
                        const value = (result as { ok: true; value: { netWeightKg: number; lotNumber: string; lotDeviationReason: string | null } }).value;
                        return {
                            stockReservationId: reservation.id,
                            expectedReservationVersion: reservation.version,
                            quantity: reservation.quantity,
                            unit: reservation.unit,
                            netWeightKg: value.netWeightKg,
                            lotNumber: value.lotNumber,
                            lotDeviationReason: value.lotDeviationReason,
                        };
                    }),
                },
                display: { customerName: order.customerName ?? String(order.customerId), lineCount: lines.length },
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
            const saved = await createDirectTradeShipment(order.id, command.payload);
            clearPending();
            await onShipped(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as ShipDirectTradeCommand);
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
            className="space-y-3 rounded-md border-2 border-violet-300 bg-violet-50/40 p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h3 id={`${idPrefix}-title`} className="text-base font-semibold text-violet-950"><span aria-hidden="true">🔒 </span>{t('sales_ship_title', { id: String(order.id) })}</h3>
                    <p className="text-xs text-gray-700">{t('sales_ship_hint')}</p>
                </div>
                {pending === null && <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('sales_close_form')}</button>}
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {pendingAlreadyApplied && (
                <p role="status" className="rounded border border-gray-400 bg-gray-100 px-2 py-1 text-sm text-gray-900">{t('sales_ship_replay_only')}</p>
            )}
            {blockedByOther && (
                <p role="status" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_ship_blocked')}</p>
            )}

            <div role="note" className="rounded border-2 border-amber-400 bg-amber-50 px-3 py-2 text-sm text-amber-950">
                <p className="font-semibold"><span aria-hidden="true">⚠ </span>{t('sales_ship_warning_title')}</p>
                <p className="mt-1">{t('sales_ship_warning')}</p>
            </div>

            <section aria-labelledby={`${idPrefix}-select`}>
                <h4 id={`${idPrefix}-select`} className="text-sm font-semibold text-gray-900">{t('sales_ship_select')}</h4>
                {rows.length === 0 ? (
                    <p className="mt-1 text-sm text-gray-700">{t('sales_ship_nothing')}</p>
                ) : (
                    <ul className="mt-1 divide-y divide-gray-200 rounded border border-violet-200 bg-white text-sm">
                        {rows.map(({ line, reservation }) => {
                            const checked = selectedIds.includes(reservation.id);
                            const shipped = isShippedAllocation(reservation);
                            const input = inputOf(reservation);
                            const index = payloadIndex(reservation.id);
                            const key = `lines[${index}]`;
                            const deviates = lotDeviates(reservation.lotNumber, input.lotNumber);
                            return (
                                <li key={reservation.id} className={`px-2 py-2 ${shipped ? 'text-gray-600' : ''}`}>
                                    <div className="flex flex-wrap items-center gap-2">
                                        <input id={`${idPrefix}-r-${reservation.id}`} type="checkbox" checked={checked} disabled={locked || shipped} onChange={() => toggle(reservation)}
                                            aria-label={t('sales_ship_pick', { vendor: reservation.vendorName ?? '', lot: reservation.lotNumber ?? t('availability_unknown') })} />
                                        <label htmlFor={`${idPrefix}-r-${reservation.id}`} className="flex flex-1 flex-wrap items-center gap-2">
                                            {shipped ? <DirectTradeShippedBadge /> : <ExpectedAllocationBadge />}
                                            <span className="text-gray-800">{sourceLabel(reservation, t('availability_unknown'))}</span>
                                            <span className="text-gray-700">{line.productName} · {line.productVariantName}</span>
                                            {reservation.expectedAt && <span className="text-xs text-violet-900">{t('sales_allocation_expected_on', { date: formatCalendarDate(reservation.expectedAt, locale) })}</span>}
                                            <span className="ml-auto font-medium">{formatQuantity(reservation.quantity, locale)}</span>
                                            <UnitBadge unit={reservation.unit} />
                                        </label>
                                    </div>
                                    {checked && !shipped && (
                                        <div className="mt-2 grid grid-cols-3 gap-3 pl-6">
                                            <div>
                                                <label htmlFor={`${idPrefix}-kg-${reservation.id}`} className="text-xs font-medium text-gray-700">{t('sales_ship_kg')} *</label>
                                                <input id={`${idPrefix}-kg-${reservation.id}`} inputMode="decimal" value={input.netKg} disabled={locked}
                                                    onChange={(event) => editRow(reservation, 'netKg', event.target.value)} aria-invalid={fieldErrors[`${key}.netWeightKg`] !== undefined} className={inputClass} />
                                                <span className="block text-xs text-gray-500">{t('sales_ship_kg_hint')}</span>
                                                <FieldError id={`${idPrefix}-kg-${reservation.id}-error`} messages={fieldErrors[`${key}.netWeightKg`]} />
                                            </div>
                                            <div>
                                                <label htmlFor={`${idPrefix}-lot-${reservation.id}`} className="text-xs font-medium text-gray-700">{t('sales_ship_lot')} *</label>
                                                <input id={`${idPrefix}-lot-${reservation.id}`} value={input.lotNumber} disabled={locked} maxLength={100}
                                                    onChange={(event) => editRow(reservation, 'lotNumber', event.target.value)} aria-invalid={fieldErrors[`${key}.lotNumber`] !== undefined} className={inputClass} />
                                                <span className="block text-xs text-gray-500">
                                                    {reservation.lotNumber ? t('sales_ship_source_lot', { lot: reservation.lotNumber }) : t('sales_ship_no_source_lot')}
                                                </span>
                                                <FieldError id={`${idPrefix}-lot-${reservation.id}-error`} messages={fieldErrors[`${key}.lotNumber`]} />
                                            </div>
                                            {deviates && (
                                                <div>
                                                    <label htmlFor={`${idPrefix}-reason-${reservation.id}`} className="text-xs font-medium text-amber-900">{t('sales_ship_lot_reason')} *</label>
                                                    <input id={`${idPrefix}-reason-${reservation.id}`} value={input.lotDeviationReason} disabled={locked} maxLength={500}
                                                        onChange={(event) => editRow(reservation, 'lotDeviationReason', event.target.value)} aria-invalid={fieldErrors[`${key}.lotDeviationReason`] !== undefined} className={inputClass} />
                                                    <span className="block text-xs text-amber-900">{t('sales_ship_lot_deviates', { source: reservation.lotNumber ?? '' })}</span>
                                                    <FieldError id={`${idPrefix}-reason-${reservation.id}-error`} messages={fieldErrors[`${key}.lotDeviationReason`]} />
                                                </div>
                                            )}
                                            <FieldError id={`${idPrefix}-q-${reservation.id}-error`} messages={fieldErrors[`${key}.quantity`]} />
                                        </div>
                                    )}
                                    {shipped && reservation.shippedOn && (
                                        <p className="mt-1 pl-6 text-xs text-gray-700">{t('sales_ship_already_shipped')}</p>
                                    )}
                                </li>
                            );
                        })}
                    </ul>
                )}
                <FieldError id={`${idPrefix}-lines-error`} messages={fieldErrors.lines} />
                <p className="mt-1 text-xs text-gray-600">{t('sales_ship_whole_only')}</p>
            </section>

            <div className="grid grid-cols-2 gap-3">
                <div>
                    <label htmlFor={`${idPrefix}-date`} className="text-xs font-medium text-gray-700">{t('sales_ship_date')} *</label>
                    <input id={`${idPrefix}-date`} type="date" value={shippedOn} max={today} disabled={locked}
                        onChange={(event) => { if (!locked) { setShippedOn(event.target.value); clearFeedback(); } }} aria-invalid={fieldErrors.shippedOn !== undefined} className={inputClass} />
                    <span className="block text-xs text-gray-500">{t('sales_ship_date_hint')}</span>
                    <FieldError id={`${idPrefix}-date-error`} messages={fieldErrors.shippedOn} />
                </div>
                <div>
                    <label htmlFor={`${idPrefix}-reference`} className="text-xs font-medium text-gray-700">{t('sales_dispatch_reference')} ({t('inspection_optional')})</label>
                    <input id={`${idPrefix}-reference`} value={reference} disabled={locked} maxLength={100} placeholder={t('sales_ship_reference_placeholder')}
                        onChange={(event) => { if (!locked) { setReference(event.target.value); clearFeedback(); } }} className={inputClass} />
                    <FieldError id={`${idPrefix}-reference-error`} messages={fieldErrors.reference} />
                    <FieldError id={`${idPrefix}-actor-error`} messages={fieldErrors.actorReference} />
                    <FieldError id={`${idPrefix}-version-error`} messages={fieldErrors.expectedOrderVersion} />
                </div>
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
                    <button type="submit" className={`${primaryButtonClass} bg-violet-700 hover:bg-violet-800`} disabled={chosen.length === 0}>
                        {t('sales_ship_confirm', { count: String(chosen.length) })}
                    </button>
                    <button type="button" className={secondaryButtonClass} onClick={onClose}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
