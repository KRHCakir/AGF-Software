'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { formatDecimalInput } from '../../lib/decimal';
import { FifoRowInput, calculatedKgFor, sumQuantities, validateFifo } from '../../lib/fifoReservation';
import { ReserveBatchCommand, reserveBatchKey, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { normalizeActor, normalizeReason } from '../../lib/salesOrderRules';
import { unitLabelKey } from '../../lib/units';
import { ApiError, toApiError } from '../../services/apiClient';
import { getFifoProposal, reserveBatch } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { FifoReservationProposal, SalesOrderLine, StockReservation } from '../../types/salesOrder';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { UnitBadge } from '../availability/QuantityCell';
import FieldError, { inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import TechnicalDetails from '../supply/TechnicalDetails';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';

const th = 'px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-gray-600';
const td = 'px-2 py-1.5 align-top';

// "Reserveren (oudste partij eerst)": het systeem stelt de pallets voor op ontvangstmoment en dan partijnummer, over zoveel pallets als
// nodig. De gebruiker past aantallen aan (kg zoals bij één reservering: berekend gewicht volgt het aantal, gewogen kg vul je in) en
// bevestigt. Eén batch van gewone exacte reserveringen, alles of niets; bevroren en bewaard tot de uitkomst bekend is (zelfde requestId).
export default function FifoReserveForm({ orderId, line, actor, onSucceeded, onReload, onCancel }: {
    orderId: number;
    line: SalesOrderLine;
    actor: string;
    onSucceeded: (saved: SaveResult<StockReservation[]>) => Promise<void>;
    onReload: () => void;
    onCancel: () => void;
}) {
    const { t, locale } = useTranslation();
    const [pending, setPending, clearPending] = usePendingCommand<ReserveBatchCommand>(reserveBatchKey(orderId, line.id));
    const [proposal, setProposal] = useState<LoadState<FifoReservationProposal>>({ status: 'loading' });
    const [token, setToken] = useState(0);
    const [rows, setRows] = useState<Record<number, FifoRowInput>>({});
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const inFlight = useRef(false);
    // Altijd de huidige bevroren opdracht (na een 409 is die weg en geldt weer het verse voorstel), zonder het voorstel opnieuw te laden.
    const pendingItems = useRef(pending?.payload.items ?? null);
    pendingItems.current = pending?.payload.items ?? null;

    useEffect(() => {
        const controller = new AbortController();
        setProposal({ status: 'loading' });
        getFifoProposal(orderId, line.id, controller.signal)
            .then((data) => {
                setProposal({ status: 'ready', data });
                // Een bevroren opdracht toont zijn eigen aantallen; anders het voorstel van het systeem.
                const items = pendingItems.current ?? data.items;
                setRows(Object.fromEntries(items.map((item) => [item.palletContentLineId, { quantity: formatDecimalInput(item.quantity), netKg: formatDecimalInput(item.netWeightKg) }])));
            })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setProposal({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [orderId, line.id, token]);

    const conflict = error !== null && error.kind === 'http' && (error.status === 409 || error.status === 404);
    const locked = submitting || pending !== null || conflict;

    const setRow = (contentId: number, field: keyof FifoRowInput, value: string) => {
        if (locked) return;
        setRows((current) => ({ ...current, [contentId]: { ...(current[contentId] ?? { quantity: '', netKg: '' }), [field]: value } }));
        setError(null);
        setFieldErrors({});
    };

    const submit = async () => {
        if (inFlight.current) return;
        let command = pending;
        if (command === null) {
            if (proposal.status !== 'ready') return;
            const result = validateFifo(proposal.data.candidates, rows, proposal.data.neededQuantity);
            const actorResult = normalizeActor(actor);
            const reasonResult = normalizeReason('', false);
            if (!result.ok || !actorResult.ok || !reasonResult.ok) {
                setFieldErrors(ruleErrorsToFieldErrors([...(result.ok ? [] : result.errors), ...(actorResult.ok ? [] : actorResult.errors)], t));
                return;
            }
            setFieldErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'reserve-batch',
                key: reserveBatchKey(orderId, line.id),
                orderId,
                lineId: line.id,
                requestId,
                payload: { requestId, unit: line.unit, actorReference: actorResult.value, reason: reasonResult.value, items: result.items },
                display: { productLabel: `${line.productName ?? ''} · ${line.productVariantName ?? ''}`, palletCount: result.items.length, quantity: result.total },
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
            const saved = await reserveBatch(orderId, line.id, command.payload);
            clearPending();
            await onSucceeded(saved);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                setPending(withAttempt(command, apiError.message) as ReserveBatchCommand);
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

    const q = (value: number) => formatQuantity(value, locale);
    // Alleen de pallets die in beeld staan tellen mee (een bevroren regel van een pallet die niet meer in het voorstel staat, niet).
    const total = proposal.status === 'ready'
        ? sumQuantities(Object.fromEntries(proposal.data.candidates.filter((c) => rows[c.palletContentLineId]).map((c) => [c.palletContentLineId, rows[c.palletContentLineId]])))
        : 0;

    return (
        <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="space-y-3 rounded border border-blue-300 bg-blue-50/40 p-3" data-testid="fifo-reserve-form">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <h4 className="text-sm font-semibold text-gray-900">{t('sales_fifo_title')}</h4>
                {pending === null && <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('sales_close_stock')}</button>}
            </div>
            <p className="text-xs text-gray-700">{t('sales_fifo_hint')}</p>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}

            {proposal.status === 'loading' && <LoadingRows rows={2} label={t('sales_fifo_loading')} />}
            {proposal.status === 'error' && <ErrorState error={proposal.error} onRetry={() => setToken((n) => n + 1)} />}
            {proposal.status === 'ready' && proposal.data.candidates.length === 0 && (
                <p className="rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm text-amber-950">{t('sales_fifo_no_stock')}</p>
            )}
            {proposal.status === 'ready' && proposal.data.candidates.length > 0 && (
                <>
                    <div className="overflow-x-auto rounded border border-gray-200 bg-white">
                        <table className="min-w-full divide-y divide-gray-200 text-sm">
                            <caption className="sr-only">{t('sales_fifo_title')}</caption>
                            <thead className="bg-gray-50">
                                <tr>
                                    <th scope="col" className={th}>{t('availability_col_lot')}</th>
                                    <th scope="col" className={th}>{t('sales_fifo_received')}</th>
                                    <th scope="col" className={th}>{t('sales_col_purchase_order')}</th>
                                    <th scope="col" className={th}>{t('availability_col_pallet')}</th>
                                    <th scope="col" className={`${th} text-right`}>{t('sales_available_on_content')}</th>
                                    <th scope="col" className={th}>{t('sales_reserve_quantity')}</th>
                                    <th scope="col" className={th}>{t('sales_reserve_kg')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {proposal.data.candidates.map((candidate, index) => {
                                    const row = rows[candidate.palletContentLineId] ?? { quantity: '', netKg: '' };
                                    const calculated = calculatedKgFor(candidate, row.quantity);
                                    const id = `fifo-${line.id}-${candidate.palletContentLineId}`;
                                    const quantityErrors = fieldErrors[`items[${candidate.palletContentLineId}].quantity`];
                                    const kgErrors = fieldErrors[`items[${candidate.palletContentLineId}].netWeightKg`];
                                    return (
                                        <tr key={candidate.palletContentLineId} className={row.quantity.trim() !== '' ? 'bg-blue-50/60' : undefined} data-fifo-row={candidate.palletContentLineId}>
                                            <td className={`${td} font-medium`}>{candidate.lotNumber}</td>
                                            <td className={`${td} whitespace-nowrap text-xs`}>{candidate.receivedAt ? formatCalendarDate(candidate.receivedAt, locale) : t('availability_unknown')}</td>
                                            <td className={`${td} font-mono text-xs`}>{candidate.supplyOrderNumber ?? t('availability_unknown')}</td>
                                            <td className={`${td} font-mono text-xs`}>{candidate.palletReference}{candidate.warehouseLocation && <span className="block text-gray-600">{candidate.warehouseLocation}</span>}</td>
                                            <td className={`${td} text-right`}>{q(candidate.availableQuantity)} <UnitBadge unit={candidate.unit} /><span className="block text-xs text-gray-600">{formatKg(candidate.availableNetWeightKg, locale)}</span></td>
                                            <td className={`${td} w-28`}>
                                                <label htmlFor={`${id}-quantity`} className="sr-only">{t('sales_reserve_quantity')} {candidate.lotNumber}</label>
                                                <input id={`${id}-quantity`} inputMode="decimal" value={row.quantity} disabled={locked} data-tour="sales-fifo-quantity" data-tour-key={index}
                                                    aria-invalid={quantityErrors !== undefined} onChange={(event) => setRow(candidate.palletContentLineId, 'quantity', event.target.value)} className={`${inputClass} !mt-0 text-right`} />
                                                <FieldError id={`${id}-quantity-error`} messages={quantityErrors} />
                                            </td>
                                            <td className={`${td} w-32`}>
                                                <label htmlFor={`${id}-kg`} className="sr-only">{t('sales_reserve_kg')} {candidate.lotNumber}</label>
                                                <input id={`${id}-kg`} inputMode="decimal" value={calculated === null ? row.netKg : formatDecimalInput(calculated)} disabled={locked || calculated !== null}
                                                    readOnly={calculated !== null} aria-invalid={kgErrors !== undefined}
                                                    onChange={(event) => setRow(candidate.palletContentLineId, 'netKg', event.target.value)} className={`${inputClass} !mt-0 text-right`} />
                                                <FieldError id={`${id}-kg-error`} messages={kgErrors} />
                                            </td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                    <p className="rounded border border-gray-300 bg-white px-2 py-1.5 text-xs text-gray-900" aria-live="polite" data-testid="fifo-total">
                        {t('sales_fifo_total', { total: q(total), needed: q(proposal.data.neededQuantity), unit: t(unitLabelKey(line.unit)).toLowerCase() })}
                    </p>
                    <FieldError id={`fifo-${line.id}-items`} messages={fieldErrors.items} />
                    <FieldError id={`fifo-${line.id}-actor`} messages={fieldErrors.actorReference} />
                </>
            )}

            {error && pending === null && (
                <div role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1.5 text-xs text-red-900">
                    <p className="font-medium">{conflict ? t('sales_conflict') : t('supply_error_http_400')}</p>
                    {error.status !== 400 && <TechnicalDetails error={error} />}
                    {conflict && (
                        <div className="mt-2 flex flex-wrap gap-2">
                            <button type="button" className={secondaryButtonClass} onClick={() => { setError(null); setToken((n) => n + 1); onReload(); }}>{t('sales_reload_and_decide')}</button>
                        </div>
                    )}
                </div>
            )}

            {!locked && proposal.status === 'ready' && proposal.data.candidates.length > 0 && (
                <div className="flex gap-2">
                    <button type="submit" data-tour="sales-fifo-confirm" className={primaryButtonClass}>{t('sales_fifo_confirm')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('inspection_cancel')}</button>
                </div>
            )}
            {submitting && <p className="text-xs text-gray-700">{t('supply_saving')}</p>}
        </form>
    );
}
