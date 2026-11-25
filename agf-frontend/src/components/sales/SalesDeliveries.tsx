'use client';

import { FormEvent, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { createSalesDelivery } from '../../services/salesOrderService';
import type { SalesOrder, StockReservation } from '../../types/salesOrder';
import Link from 'next/link';
import { createInvoiceFromDeliveries } from '../../services/salesInvoiceService';
import type { DeliveryInvoicing } from '../../types/salesInvoice';
import PartijNumberLink from '../partij/PartijNumberLink';
import ErrorNotice from '../supply/ErrorNotice';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Leveringen van een verkooporder (feedback 22 november): een verkoop-partij is één levering aan één klant. De eerste levering bestaat
// altijd; Deellevering maakt een tweede levering met een nieuw V-nummer en een eigen laadbon. De gebruiker kiest welke gereserveerde pallets
// met de nieuwe levering meegaan; wat nog niet gereserveerd is, gaat altijd mee met de nieuwste levering. Al verzonden pallets blijven bij
// hun levering. Eén sleutel per poging: een herhaling na een netwerkfout maakt geen tweede levering.
// Onzekere opdrachten per order in sessionStorage: een verversing of heropende pagina gebruikt dezelfde sleutel en keuze (de server herkent de herhaling).
const PENDING_KEY = 'agf_sales_delivery_pending';
interface StoredDelivery { requestId: string; chosen: number[]; posted: boolean }
function readStored(): Record<string, StoredDelivery> {
    try {
        const parsed: unknown = JSON.parse(window.sessionStorage.getItem(PENDING_KEY) ?? '{}');
        return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed as Record<string, StoredDelivery> : {};
    } catch { return {}; }
}
function writeStored(orderId: number, value: StoredDelivery | null) {
    try {
        const all = readStored();
        if (value === null) delete all[String(orderId)]; else all[String(orderId)] = value;
        if (Object.keys(all).length === 0) window.sessionStorage.removeItem(PENDING_KEY);
        else window.sessionStorage.setItem(PENDING_KEY, JSON.stringify(all));
    } catch { /* geen opslag (privémodus): de opdracht blijft dan alleen in het geheugen */ }
}
function loadStored(orderId: number): { requestId: string; chosen: number[] } | null {
    const stored = readStored()[String(orderId)];
    if (!stored || typeof stored.requestId !== 'string' || !Array.isArray(stored.chosen)) return null;
    // Al verstuurd vóór de pagina verdween: de order is nu vers geladen, dus de opdracht is verwerkt en afgerond.
    if (stored.posted) { writeStored(orderId, null); return null; }
    return { requestId: stored.requestId, chosen: stored.chosen };
}

// Verkoopfactuur 1: per levering Factureren (een concept met de rest van die levering), of het open concept en de definitieve facturen.
// Een herhaling na een netwerkfout gebruikt dezelfde sleutel (geen tweede concept).
function DeliveryInvoice({ deliveryId, info, onChanged }: { deliveryId: number; info: DeliveryInvoicing | undefined; onChanged?: () => void }) {
    const { t } = useTranslation();
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [created, setCreated] = useState<number | null>(null);
    const key = useRef<string | null>(null);
    if (info === undefined) return null;
    const finals = info.invoices.filter((invoice) => invoice.status === 'Final');
    const concept = created ?? info.openConceptId;
    const invoice = async () => {
        key.current ??= newRequestId();
        setBusy(true);
        setError(null);
        try {
            const result = await createInvoiceFromDeliveries(key.current, [deliveryId]);
            key.current = null;
            setCreated(result.value.id);
            onChanged?.();
        } catch (caught) {
            const apiError = toApiError(caught);
            if (apiError.isDefinitiveRejection) key.current = null;
            setError(apiError);
        } finally {
            setBusy(false);
        }
    };
    return (
        <span className="flex flex-wrap items-baseline gap-x-2" data-testid="so-delivery-invoicing">
            {finals.map((final) => (
                <Link key={final.id} href={`/sales-invoices?id=${final.id}`} className="font-mono text-blue-800 underline underline-offset-2">{final.invoiceNumber}</Link>
            ))}
            {concept !== null && (
                <Link href={`/sales-invoices?id=${concept}`} data-tour="so-delivery-invoice-open" data-tour-key={String(deliveryId)} className="font-medium text-blue-800 underline underline-offset-2">{t('so_invoice_open_concept')}</Link>
            )}
            {concept === null && info.hasRest && (
                <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void invoice()} data-tour="so-delivery-invoice" data-tour-key={String(deliveryId)}>{t('so_invoice_delivery')}</button>
            )}
            {concept === null && !info.hasRest && finals.length > 0 && <span className="text-xs text-gray-600">{t('so_invoice_done')}</span>}
            {error && <span className="basis-full"><ErrorNotice error={error} /></span>}
        </span>
    );
}

export default function SalesDeliveries({ order, readOnly, onChanged, invoicing, onInvoicingChanged }: {
    order: SalesOrder; readOnly: boolean; onChanged: () => Promise<unknown> | void; invoicing?: DeliveryInvoicing[]; onInvoicingChanged?: () => void;
}) {
    const { t, locale } = useTranslation();
    const deliveries = [...(order.deliveries ?? [])].sort((a, b) => a.sequence - b.sequence);
    const [open, setOpen] = useState(false);
    const [chosen, setChosen] = useState<number[]>([]);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    // De levering is aangemaakt maar de order kon nog niet worden ververst: geen nieuwe opdracht tot het verversen lukt.
    const [refreshFailed, setRefreshFailed] = useState(false);
    const requestId = useRef<string | null>(null);
    // Opdracht met onzekere uitkomst (netwerk-/vormfout): blijft ook na Annuleren bewaard, zodat heropenen dezelfde sleutel en keuze gebruikt.
    const uncertain = useRef<{ requestId: string; chosen: number[] } | null>(null);
    // Zolang die onzekere opdracht er is, ligt de keuze vast (review): alleen een definitief antwoord op dezelfde sleutel (aangemaakt, herkend
    // of geweigerd) geeft haar vrij. De order opnieuw lezen is niet genoeg: de eerste POST kan op de server nog lopen.
    const [locked, setLocked] = useState(false);

    useEffect(() => {
        setOpen(false); setChosen([]); setError(null); setRefreshFailed(false); requestId.current = null;
        uncertain.current = loadStored(order.id);
        setLocked(uncertain.current !== null);
    }, [order.id]);

    // Per levering tellen ook de al verzonden pallets mee (review); vrijgegeven of teruggeboekte reserveringen niet. Kiezen voor een
    // deellevering kan alleen uit actieve reserveringen.
    const counted = order.lines.flatMap((line) => line.reservations).filter((reservation) => reservation.status === 'Active' || reservation.status === 'Consumed');
    const reservations = order.lines.flatMap((line) => line.reservations.map((reservation) => ({ line, reservation })))
        .filter(({ reservation }) => reservation.status === 'Active');
    const productOf = (lineId: number) => {
        const line = order.lines.find((l) => l.id === lineId);
        return line ? [line.productName, line.productVariantName].filter(Boolean).join(' ') : '';
    };
    const deliveryOf = (reservation: StockReservation) => deliveries.find((d) => d.reservationIds.includes(reservation.id)) ?? deliveries[0];
    const canSplit = !readOnly && !refreshFailed && order.status === 'Open' && order.lines.length > 0 && deliveries.length > 0;

    // Ook een enige levering van een gesloten order blijft zichtbaar; canSplit bepaalt alleen de knop Deellevering.
    if (deliveries.length === 0) return null;

    const start = () => {
        const pending = uncertain.current;
        requestId.current = pending?.requestId ?? newRequestId();
        setChosen(pending?.chosen ?? []);
        setLocked(pending !== null);
        setError(null);
        setOpen(true);
    };
    const toggle = (id: number) => {
        if (locked) return;
        // Een andere keuze is een andere opdracht: dan ook een nieuwe sleutel (anders weigert de server de herhaling).
        requestId.current = newRequestId();
        setChosen((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));
    };
    // Verversen na een geslaagde POST: pas als de order bijgewerkt is, vervalt de bewaarde opdracht.
    const refresh = async () => {
        let ok = true;
        try { ok = (await onChanged()) !== false; } catch { ok = false; }
        if (ok) { uncertain.current = null; writeStored(order.id, null); setLocked(false); }
        setRefreshFailed(!ok);
        return ok;
    };
    const retryRefresh = async () => {
        if (busy) return;
        setBusy(true);
        try { await refresh(); } finally { setBusy(false); }
    };
    const submit = async (event: FormEvent) => {
        event.preventDefault();
        const key = requestId.current;
        if (busy || key === null) return;
        setBusy(true);
        setError(null);
        // Vooraf bewaren: ook als de pagina tijdens het verzenden verdwijnt, blijft de sleutel beschikbaar.
        uncertain.current = { requestId: key, chosen };
        writeStored(order.id, { requestId: key, chosen, posted: false });
        try {
            await createSalesDelivery(order.id, { requestId: key, reservationIds: chosen });
            writeStored(order.id, { requestId: key, chosen, posted: true });
            setOpen(false);
            requestId.current = null;
            await refresh();
        } catch (caught) {
            // Dezelfde sleutel blijft staan: opnieuw proberen is dezelfde opdracht.
            const apiError = toApiError(caught);
            if (apiError.isDefinitiveRejection) { uncertain.current = null; writeStored(order.id, null); setLocked(false); } else { setLocked(true); }
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
                        <span className="text-gray-700" data-testid="so-delivery-pallets" data-count={counted.filter((reservation) => delivery.reservationIds.includes(reservation.id)).length}>
                            {t('so_delivery_pallets', { count: String(counted.filter((reservation) => delivery.reservationIds.includes(reservation.id)).length) })}
                        </span>
                        {delivery.last && deliveries.length > 1 && <span className="text-xs text-gray-600">{t('so_delivery_rest')}</span>}
                        <span className="ml-auto"><DeliveryInvoice deliveryId={delivery.id} info={invoicing?.find((d) => d.salesDeliveryId === delivery.id)} onChanged={onInvoicingChanged} /></span>
                    </li>
                ))}
            </ul>
            {refreshFailed && (
                <div role="alert" className="space-y-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-950" data-testid="so-delivery-refresh-failed">
                    <p>{t('sales_refresh_failed')}</p>
                    <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void retryRefresh()}>{t('sales_refresh')}</button>
                </div>
            )}
            {open && (
                <form onSubmit={(event) => void submit(event)} className="space-y-2 rounded border border-blue-200 bg-blue-50 px-3 py-2" data-testid="so-partial-delivery-form">
                    <p className="text-sm text-blue-950">{t('so_partial_delivery_text')}</p>
                    {reservations.length === 0
                        ? <p className="text-sm text-gray-700">{t('so_partial_delivery_nothing_reserved')}</p>
                        : (
                            <fieldset className="space-y-1" disabled={busy || locked}>
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
                    {locked && (
                        <div className="space-y-1 text-sm text-amber-950" data-testid="so-partial-delivery-uncertain">
                            <p>{t('so_partial_delivery_uncertain')}</p>
                        </div>
                    )}
                </form>
            )}
        </section>
    );
}
