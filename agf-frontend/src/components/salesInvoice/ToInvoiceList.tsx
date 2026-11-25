'use client';

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { newRequestId } from '../../lib/requestId';
import { toApiError, type ApiError } from '../../services/apiClient';
import { createInvoiceFromDeliveries, getToInvoice } from '../../services/salesInvoiceService';
import type { InvoicingCustomer } from '../../types/salesInvoice';
import ErrorNotice from '../supply/ErrorNotice';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Verkoopfactuur 1: de werklijst Nog te factureren. Leveringen met een rest, per klant, met het aantal dagen sinds levering. Per klant
// staan de leveringen zonder open concept aangevinkt; Factuur maken zet ze samen in één concept (één klant per factuur). Een levering met
// een open concept heeft een link naar dat concept.
export default function ToInvoiceList({ token, onCreated }: { token: number; onCreated: (id: number) => void }) {
    const { t, locale } = useTranslation();
    const [customers, setCustomers] = useState<InvoicingCustomer[] | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [chosen, setChosen] = useState<Record<number, Set<number>>>({});
    const [busy, setBusy] = useState<number | null>(null);
    const [error, setError] = useState<{ customerId: number; error: ApiError } | null>(null);
    const keys = useRef<Record<string, string>>({});

    useEffect(() => {
        const controller = new AbortController();
        getToInvoice(controller.signal)
            .then((data) => {
                setCustomers(data);
                setLoadError(null);
                setChosen(Object.fromEntries(data.map((c) => [c.customerId, new Set(c.deliveries.filter((d) => d.openConceptId === null && d.hasRest).map((d) => d.salesDeliveryId))])));
            })
            .catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, [token]);

    if (loadError) return <ErrorNotice error={loadError} />;
    if (customers === null || customers.length === 0) return null;

    const toggle = (customerId: number, deliveryId: number, on: boolean) => setChosen((current) => {
        const next = new Set(current[customerId] ?? []);
        if (on) next.add(deliveryId); else next.delete(deliveryId);
        return { ...current, [customerId]: next };
    });
    const create = async (customer: InvoicingCustomer) => {
        const ids = [...(chosen[customer.customerId] ?? [])].sort((a, b) => a - b);
        // Dezelfde keuze na een onzekere fout: dezelfde sleutel, dus nooit een tweede concept.
        const key = `${customer.customerId}:${ids.join(',')}`;
        keys.current[key] ??= newRequestId();
        setBusy(customer.customerId);
        setError(null);
        try {
            const result = await createInvoiceFromDeliveries(keys.current[key], ids);
            delete keys.current[key];
            onCreated(result.value.id);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (apiError.isDefinitiveRejection) delete keys.current[key];
            setError({ customerId: customer.customerId, error: apiError });
        } finally {
            setBusy(null);
        }
    };

    return (
        <section className="space-y-2 rounded-md border border-amber-300 bg-amber-50 p-3" data-testid="si-to-invoice" data-tour="si-to-invoice" aria-labelledby="si-to-invoice-title">
            <h2 id="si-to-invoice-title" className="font-semibold text-amber-950">{t('si_to_invoice_title')}</h2>
            <p className="text-sm text-amber-950">{t('si_to_invoice_text')}</p>
            {customers.map((customer) => (
                <div key={customer.customerId} className="rounded border border-amber-200 bg-white p-2" data-testid="si-to-invoice-customer">
                    <h3 className="font-medium text-gray-900">{customer.customerName ?? '–'}</h3>
                    <ul className="space-y-1 text-sm">
                        {customer.deliveries.map((delivery) => {
                            const rest = delivery.lines.filter((line) => line.rest > 0);
                            return (
                                <li key={delivery.salesDeliveryId} className="flex flex-wrap items-baseline gap-x-2">
                                    {delivery.openConceptId === null ? (
                                        <input type="checkbox" className="h-4 w-4" aria-label={t('si_to_invoice_choose', { order: delivery.orderNumber, sequence: String(delivery.sequence) })}
                                            data-tour="si-to-invoice-choose" data-tour-key={String(delivery.salesDeliveryId)}
                                            checked={chosen[customer.customerId]?.has(delivery.salesDeliveryId) ?? false}
                                            onChange={(event) => toggle(customer.customerId, delivery.salesDeliveryId, event.target.checked)} />
                                    ) : <span className="inline-block h-4 w-4" aria-hidden="true" />}
                                    <Link href={`/sales-orders?id=${delivery.salesOrderId}`} className="text-blue-800 underline underline-offset-2">{delivery.orderNumber}</Link>
                                    <span>{t('so_delivery_label', { sequence: String(delivery.sequence) })}</span>
                                    {delivery.partijNumber && <span className="font-mono">{delivery.partijNumber}</span>}
                                    <span className="text-gray-700">{delivery.deliveredAt ? formatCalendarDate(delivery.deliveredAt, locale) : '–'}</span>
                                    {delivery.daysSinceDelivery !== null && <span className="text-xs text-gray-700" data-testid="si-days">{t('si_days_since', { days: String(delivery.daysSinceDelivery) })}</span>}
                                    <span className="text-gray-700">{rest.map((line) => `${formatQuantity(line.rest, locale)} ${line.unit} ${line.description ?? ''}`.trim()).join(' · ')}</span>
                                    {delivery.openConceptId !== null && <Link href={`/sales-invoices?id=${delivery.openConceptId}`} className="text-blue-800 underline underline-offset-2">{t('so_invoice_open_concept')}</Link>}
                                </li>
                            );
                        })}
                    </ul>
                    <button type="button" className={`${(chosen[customer.customerId]?.size ?? 0) > 0 ? primaryButtonClass : secondaryButtonClass} mt-2`} data-tour="si-to-invoice-create"
                        data-tour-key={String(customer.customerId)} disabled={busy !== null || (chosen[customer.customerId]?.size ?? 0) === 0} onClick={() => void create(customer)}>
                        {t('si_to_invoice_create')}
                    </button>
                    {error?.customerId === customer.customerId && <ErrorNotice error={error.error} />}
                </div>
            ))}
        </section>
    );
}
