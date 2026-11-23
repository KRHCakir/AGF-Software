'use client';

import { FormEvent, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { createSalesDelivery } from '../../services/salesOrderService';
import type { SalesOrder, StockReservation } from '../../types/salesOrder';
import PartijNumberLink from '../partij/PartijNumberLink';
import ErrorNotice from '../supply/ErrorNotice';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Leveringen van een verkooporder (feedback 22 november): een verkoop-partij is één levering aan één klant. De eerste levering bestaat
// altijd; Deellevering maakt een tweede levering met een nieuw V-nummer en een eigen laadbon. De gebruiker kiest welke gereserveerde pallets
// met de nieuwe levering meegaan; wat nog niet gereserveerd is, gaat altijd mee met de nieuwste levering. Al verzonden pallets blijven bij
// hun levering. Eén sleutel per poging: een herhaling na een netwerkfout maakt geen tweede levering.
export default function SalesDeliveries({ order, readOnly, onChanged }: { order: SalesOrder; readOnly: boolean; onChanged: () => Promise<unknown> | void }) {
    const { t, locale } = useTranslation();
    const deliveries = [...(order.deliveries ?? [])].sort((a, b) => a.sequence - b.sequence);
    const [open, setOpen] = useState(false);
    const [chosen, setChosen] = useState<number[]>([]);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const requestId = useRef<string | null>(null);
    // Opdracht met onzekere uitkomst (netwerk-/vormfout): blijft ook na Annuleren bewaard, zodat heropenen dezelfde sleutel en keuze gebruikt.
    const uncertain = useRef<{ requestId: string; chosen: number[] } | null>(null);

    useEffect(() => { setOpen(false); setChosen([]); setError(null); requestId.current = null; uncertain.current = null; }, [order.id]);

    const reservations = order.lines.flatMap((line) => line.reservations.map((reservation) => ({ line, reservation })))
        .filter(({ reservation }) => reservation.status === 'Active');
    const productOf = (lineId: number) => {
        const line = order.lines.find((l) => l.id === lineId);
        return line ? [line.productName, line.productVariantName].filter(Boolean).join(' ') : '';
    };
    const deliveryOf = (reservation: StockReservation) => deliveries.find((d) => d.reservationIds.includes(reservation.id)) ?? deliveries[0];
    const canSplit = !readOnly && order.status === 'Open' && order.lines.length > 0 && deliveries.length > 0;

    // Ook een enige levering van een gesloten order blijft zichtbaar; canSplit bepaalt alleen de knop Deellevering.
    if (deliveries.length === 0) return null;

    const start = () => {
        const pending = uncertain.current;
        requestId.current = pending?.requestId ?? newRequestId();
        setChosen(pending?.chosen ?? []);
        setError(null);
        setOpen(true);
    };
    const toggle = (id: number) => {
        // Een andere keuze is een andere opdracht: dan ook een nieuwe sleutel (anders weigert de server de herhaling).
        requestId.current = newRequestId();
        uncertain.current = null;
        setChosen((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));
    };
    const submit = async (event: FormEvent) => {
        event.preventDefault();
        const key = requestId.current;
        if (busy || key === null) return;
        setBusy(true);
        setError(null);
        try {
            await createSalesDelivery(order.id, { requestId: key, reservationIds: chosen });
            setOpen(false);
            requestId.current = null;
            uncertain.current = null;
            await onChanged();
        } catch (caught) {
            // Dezelfde sleutel blijft staan: opnieuw proberen is dezelfde opdracht.
            const apiError = toApiError(caught);
            uncertain.current = apiError.isDefinitiveRejection ? null : { requestId: key, chosen };
            setError(apiError);
        } finally {
            setBusy(false);
        }
    };

    return (
        <section aria-labelledby={`deliveries-${order.id}`} className="space-y-2 rounded-md border border-gray-300 bg-white px-4 py-3" data-testid="so-deliveries" data-tour="so-deliveries">
            <div className="flex flex-wrap items-center gap-2">
                <h3 id={`deliveries-${order.id}`} className="font-semibold text-gray-900">{t('so_deliveries')}</h3>
                {canSplit && !open && (
                    <button type="button" className={`${secondaryButtonClass} ml-auto`} onClick={start} data-tour="so-partial-delivery">{t('so_partial_delivery')}</button>
                )}
            </div>
            <ul className="space-y-1 text-sm">
                {deliveries.map((delivery) => (
                    <li key={delivery.id} className="flex flex-wrap items-baseline gap-x-2" data-testid="so-delivery" data-delivery-sequence={delivery.sequence}>
                        <span className="font-semibold">{t('so_delivery_label', { sequence: String(delivery.sequence) })}</span>
                        {delivery.partijNumber !== null && (
                            <span data-tour="so-delivery-partij" data-tour-key={delivery.partijNumber}>
                                <PartijNumberLink partijId={delivery.partijId} partijNumber={delivery.partijNumber} lotNumber={delivery.partijNumber} />
                            </span>
                        )}
                        <span className="text-gray-700">{t('so_delivery_pallets', { count: String(reservations.filter(({ reservation }) => delivery.reservationIds.includes(reservation.id)).length) })}</span>
                        {delivery.last && deliveries.length > 1 && <span className="text-xs text-gray-600">{t('so_delivery_rest')}</span>}
                    </li>
                ))}
            </ul>
            {open && (
                <form onSubmit={(event) => void submit(event)} className="space-y-2 rounded border border-blue-200 bg-blue-50 px-3 py-2" data-testid="so-partial-delivery-form">
                    <p className="text-sm text-blue-950">{t('so_partial_delivery_text')}</p>
                    {reservations.length === 0
                        ? <p className="text-sm text-gray-700">{t('so_partial_delivery_nothing_reserved')}</p>
                        : (
                            <fieldset className="space-y-1" disabled={busy}>
                                <legend className="text-xs font-medium text-gray-700">{t('so_partial_delivery_choose')}</legend>
                                {reservations.map(({ line, reservation }) => {
                                    const current = deliveryOf(reservation);
                                    return (
                                        <label key={reservation.id} className="flex flex-wrap items-center gap-2 text-sm">
                                            <input type="checkbox" checked={chosen.includes(reservation.id)} onChange={() => toggle(reservation.id)} data-tour="so-partial-delivery-reservation" />
                                            <span className="font-mono">{reservation.palletReference ?? t('availability_unknown')}</span>
                                            <span>{productOf(line.id)}</span>
                                            <span className="tabular-nums">{formatQuantity(reservation.quantity, locale)} {reservation.unit}</span>
                                            {reservation.partijNumber && <span className="text-gray-700">{t('so_partial_delivery_lot', { lot: reservation.partijNumber })}</span>}
                                            {current && deliveries.length > 1 && <span className="text-xs text-gray-600">{t('so_delivery_label', { sequence: String(current.sequence) })}</span>}
                                        </label>
                                    );
                                })}
                            </fieldset>
                        )}
                    {error && <ErrorNotice error={error} />}
                    <div className="flex flex-wrap gap-2">
                        <button type="submit" className={primaryButtonClass} disabled={busy} data-tour="so-partial-delivery-create">
                            {busy ? t('supply_saving') : t('so_partial_delivery_create', { count: String(chosen.length) })}
                        </button>
                        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => { setOpen(false); requestId.current = null; }}>{t('cancel')}</button>
                    </div>
                </form>
            )}
        </section>
    );
}
