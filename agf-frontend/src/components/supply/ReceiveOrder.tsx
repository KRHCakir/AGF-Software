'use client';

import Link from 'next/link';
import { KeyboardEvent, useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity } from '../../lib/availability';
import { DraftError, ReceiveDraft, draftFor, openLines, progressOf, referenceInputs, requestFor, requestKey } from '../../lib/receipt';
import { newRequestId } from '../../lib/requestId';
import { toApiError } from '../../services/apiClient';
import { getReceiptOrder, receiveLine } from '../../services/receiptService';
import type { PackageUnit } from '../../types/availability';
import type { ReceiptLine, ReceiptOrder } from '../../types/receipt';
import QualityBadge from '../availability/QualityBadge';
import { UnitBadge } from '../availability/QuantityCell';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { checkLines } from '../certificates/CertificateCheckDialog';
import OrderNumberText from '../purchase/OrderNumberText';
import { inputClass } from './FieldError';
import { decimalErrorKey } from './messages';
import { primaryButtonClass, secondaryButtonClass } from './SuccessPanel';

// Ontvangen in één scherm (feedback 22 november): de lijst "Dit wordt verwacht" van één inkooporder, met per regel besteld, al ontvangen
// en nog te ontvangen, en één knop Ontvangen (het openstaande aantal, of een afwijkend aantal in het veld). Bovenaan Alles ontvangen.
// Geen controlevragen: komt er iets anders, dan pas je de orderregel aan (Inkooporder › Aanpassen). Optioneel per regel: Palletnummers
// (aantal pallets en per pallet een nummer, typen of scannen) en Keuren / afwijzen (de bestaande keuring, ook later te doen).
// Toetsenbord: Enter in het aantal (of kg) ontvangt die regel; Tab gaat aantal → Ontvangen → Palletnummers → volgende regel.
export default function ReceiveOrder({ orderId }: { orderId: number }) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<ReceiptOrder>>({ status: 'loading' });
    const [token, setToken] = useState(0);
    const [drafts, setDrafts] = useState<Record<number, ReceiveDraft>>({});
    const [busy, setBusy] = useState<number | 'all' | null>(null);
    const [errors, setErrors] = useState<Record<number, string[]>>({});
    const [notice, setNotice] = useState<string | null>(null);
    const requests = useRef(new Map<number, { key: string; requestId: string }>());
    const inFlight = useRef(false);

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        getReceiptOrder(orderId, controller.signal)
            .then((order) => {
                setState({ status: 'ready', data: order });
                setDrafts(Object.fromEntries(order.lines.map((line) => [line.id, draftFor(line)])));
            })
            .catch((caught: unknown) => {
                const error = toApiError(caught);
                if (error.kind !== 'aborted') setState({ status: 'error', error });
            });
        return () => controller.abort();
    }, [orderId, token]);

    const edit = (lineId: number, change: Partial<ReceiveDraft>) => {
        setDrafts((current) => ({ ...current, [lineId]: { ...current[lineId], ...change } }));
        setErrors((current) => { const next = { ...current }; delete next[lineId]; return next; });
    };

    const draftErrorText = useCallback((error: DraftError) => {
        if (error.error === 'kg_required') return t('receipt_error_kg_required');
        if (error.error === 'pallet_range') return t('receipt_error_pallet_range');
        return `${t(`receipt_field_${error.field}`)}: ${t(decimalErrorKey(error.error))}`;
    }, [t]);

    // Eén regel ontvangen; true als het gelukt is. De server geeft de bijgewerkte order terug (ook wat er nu nog open staat).
    const receive = async (line: ReceiptLine, draft: ReceiveDraft): Promise<boolean> => {
        const built = requestFor(line, draft);
        if (!built.ok) {
            setErrors((current) => ({ ...current, [line.id]: built.errors.map(draftErrorText) }));
            return false;
        }
        const key = requestKey(line.id, built.request);
        let request = requests.current.get(line.id);
        if (!request || request.key !== key) {
            request = { key, requestId: newRequestId() };
            requests.current.set(line.id, request);
        }
        try {
            const result = await receiveLine(orderId, line.id, { ...built.request, requestId: request.requestId });
            requests.current.delete(line.id);
            setErrors((current) => { const next = { ...current }; delete next[line.id]; return next; });
            setState({ status: 'ready', data: result.order });
            const updated = result.order.lines.find((candidate) => candidate.id === line.id);
            setDrafts((current) => ({ ...current, [line.id]: updated ? draftFor(updated) : current[line.id] }));
            setNotice(t('receipt_received_notice', {
                quantity: formatQuantity(result.quantity, locale),
                product: `${line.productName ?? ''} ${line.productVariantName ?? ''}`.trim(),
                pallets: result.pallets.map((pallet) => pallet.reference).join(', '),
            }));
            return true;
        } catch (caught) {
            const error = toApiError(caught);
            const messages = Object.values(error.fieldErrors).flat();
            setErrors((current) => ({ ...current, [line.id]: messages.length > 0 ? messages : [error.message] }));
            return false;
        }
    };

    const receiveOne = async (line: ReceiptLine) => {
        if (inFlight.current) return;
        inFlight.current = true;
        setBusy(line.id);
        setNotice(null);
        try {
            await receive(line, drafts[line.id] ?? draftFor(line));
        } finally {
            inFlight.current = false;
            setBusy(null);
        }
    };

    // Alles ontvangen: regel voor regel het ingevulde aantal; een regel die niet lukt, blijft met haar melding staan en de rest gaat door.
    const receiveAll = async () => {
        if (inFlight.current || state.status !== 'ready') return;
        inFlight.current = true;
        setBusy('all');
        setNotice(null);
        let received = 0;
        try {
            for (const line of openLines(state.data)) {
                if (await receive(line, drafts[line.id] ?? draftFor(line))) received += 1;
            }
        } finally {
            inFlight.current = false;
            setBusy(null);
        }
        if (received > 0) setNotice(t('receipt_received_all_notice', { count: String(received) }));
    };

    const enterReceives = (line: ReceiptLine) => (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key !== 'Enter') return;
        event.preventDefault();
        void receiveOne(line);
    };

    if (state.status === 'loading') return <LoadingRows rows={4} label={t('supply_loading_sources')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setToken((n) => n + 1)} />;
    const order = state.data;
    const open = openLines(order);

    return (
        <section aria-labelledby="receipt-expected" className="space-y-4 rounded-md border border-gray-300 bg-white p-4" data-testid="receive-order">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0">
                    <h2 id="receipt-expected" className="text-lg font-semibold text-gray-900" data-tour="receive-expected">{t('receipt_expected_title')}</h2>
                    <p className="flex flex-wrap gap-x-1.5 text-sm text-gray-700">
                        <span className="font-medium text-gray-900"><OrderNumberText id={order.orderId} orderNumber={order.orderNumber} supplierReference={order.supplierReference} /></span>
                        <span>· {order.vendorName ?? '–'}</span>
                        {order.expectedAt && <span>· {t('supply_expected_on', { date: formatDateTime(order.expectedAt, locale) })}</span>}
                        {order.partijNumber && <span>· {t('supply_lot')} <span className="font-mono font-semibold">{order.partijNumber}</span></span>}
                    </p>
                    <p className="mt-1 text-sm text-gray-600">{t('receipt_expected_hint')}</p>
                </div>
                {order.receivable && open.length > 0 && (
                    <button type="button" className={`${primaryButtonClass} shrink-0`} data-tour="receive-all" disabled={busy !== null} onClick={() => void receiveAll()}>
                        {busy === 'all' ? t('receipt_receiving') : t('receipt_receive_all')}
                    </button>
                )}
            </div>

            {!order.receivable && (
                <p role="alert" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">{t('receipt_not_receivable', { reason: order.notReceivableReason ?? '' })}</p>
            )}
            {/* Certificaten deel 1: een regel op het scherm in plaats van een venster; ontvangen legt vast wie hem zag. */}
            {order.certificateWarnings.length > 0 && (
                <ul className="space-y-1 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900" data-tour="receive-certificate-warning">
                    {order.certificateWarnings.map((check) => (
                        <li key={check.productId}><span aria-hidden="true">⚠ </span>{checkLines(check, t, locale).join(' ')}</li>
                    ))}
                </ul>
            )}
            {notice && <p role="status" className="rounded border border-emerald-300 bg-emerald-50 px-3 py-2 text-sm text-emerald-900" data-tour="receive-notice">{notice}</p>}

            {order.lines.length === 0 && <p className="text-sm text-gray-700">{t('receipt_no_lines')}</p>}
            {order.lines.length > 0 && (
                <table className="block w-full text-sm md:table">
                    <thead className="hidden bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600 md:table-header-group">
                        <tr>
                            <th scope="col" className="px-2 py-1.5">{t('supply_variant')}</th>
                            <th scope="col" className="px-2 py-1.5">{t('supply_lot')}</th>
                            <th scope="col" className="px-2 py-1.5 text-right">{t('receipt_ordered')}</th>
                            <th scope="col" className="px-2 py-1.5 text-right">{t('receipt_received')}</th>
                            <th scope="col" className="px-2 py-1.5 text-right">{t('receipt_open')}</th>
                            <th scope="col" className="px-2 py-1.5">{t('receipt_quantity')}</th>
                            <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('receipt_actions')}</span></th>
                        </tr>
                    </thead>
                    {order.lines.map((line) => (
                        <ReceiveLineRows key={line.id} line={line} draft={drafts[line.id] ?? draftFor(line)} errors={errors[line.id]} busy={busy}
                            receivable={order.receivable && line.receivable} onEdit={(change) => edit(line.id, change)} onReceive={() => void receiveOne(line)}
                            onEnter={enterReceives(line)} />
                    ))}
                </table>
            )}
        </section>
    );
}

function ReceiveLineRows({ line, draft, errors, busy, receivable, onEdit, onReceive, onEnter }: {
    line: ReceiptLine;
    draft: ReceiveDraft;
    errors: string[] | undefined;
    busy: number | 'all' | null;
    receivable: boolean;
    onEdit: (change: Partial<ReceiveDraft>) => void;
    onReceive: () => void;
    onEnter: (event: KeyboardEvent<HTMLInputElement>) => void;
}) {
    const { t, locale } = useTranslation();
    const progress = progressOf(line);
    const errorId = `receive-error-${line.id}`;
    const toInspect = line.pallets.filter((pallet) => pallet.qualityStatus === 'Quarantine');
    const cell = 'px-2 py-1 align-top md:py-1.5';
    const label = (key: string) => <span className="text-xs text-gray-600 md:hidden">{t(key)}: </span>;
    return (
        <tbody className="block border-t border-gray-200 py-2 md:table-row-group md:py-0" data-testid={`receive-line-${line.id}`}>
            <tr className={`block md:table-row ${progress === 'complete' ? 'bg-emerald-50/60' : ''}`}>
                <td className={`${cell} block md:table-cell`}>
                    <span className="font-medium text-gray-900">{line.productName} · {line.productVariantName}</span>
                    {(line.origin || line.size) && <span className="block text-xs text-gray-600">{[line.origin, line.size].filter(Boolean).join(' · ')}</span>}
                </td>
                <td className={`${cell} block md:table-cell`}>{label('supply_lot')}<span className="font-mono">{line.partijNumber ?? '–'}</span></td>
                <td className={`${cell} block md:table-cell md:text-right`}>{label('receipt_ordered')}{formatQuantity(line.orderedQuantity, locale)} {line.unit && <UnitBadge unit={line.unit as PackageUnit} />}</td>
                <td className={`${cell} block md:table-cell md:text-right`} data-tour="receive-line-received" data-tour-key={line.id}>{label('receipt_received')}{formatQuantity(line.receivedQuantity, locale)}</td>
                <td className={`${cell} block font-semibold md:table-cell md:text-right`}>{label('receipt_open')}{formatQuantity(line.openQuantity, locale)}</td>
                <td className={`${cell} block md:table-cell`}>
                    {receivable && (
                        <div className="flex flex-wrap gap-2">
                            <label className="text-xs text-gray-700">
                                <span className="sr-only md:not-sr-only md:block">{t('receipt_quantity')}</span>
                                <span className="md:hidden">{t('receipt_quantity')} </span>
                                <input inputMode="decimal" value={draft.quantity} onChange={(event) => onEdit({ quantity: event.target.value })} onKeyDown={onEnter}
                                    aria-describedby={errors ? errorId : undefined} aria-invalid={errors ? true : undefined}
                                    data-tour="receive-quantity" data-tour-key={line.id} className={`${inputClass} w-24 text-right`} />
                            </label>
                            {line.netKgPerPackage === null && (
                                <label className="text-xs text-gray-700">
                                    <span className="md:block">{t('receipt_net_kg')}</span>
                                    <input inputMode="decimal" value={draft.netKg} onChange={(event) => onEdit({ netKg: event.target.value })} onKeyDown={onEnter}
                                        data-tour="receive-net-kg" data-tour-key={line.id} className={`${inputClass} w-28 text-right`} />
                                </label>
                            )}
                        </div>
                    )}
                </td>
                <td className={`${cell} block md:table-cell`}>
                    <div className="flex flex-wrap gap-2">
                        {receivable && (
                            <button type="button" className={primaryButtonClass} disabled={busy !== null} onClick={onReceive} data-tour="receive-line-button" data-tour-key={line.id}>
                                {busy === line.id ? t('receipt_receiving') : t('receipt_receive')}
                            </button>
                        )}
                        {receivable && (
                            <button type="button" className={secondaryButtonClass} aria-expanded={draft.palletsOpen} aria-controls={`receive-pallets-${line.id}`}
                                onClick={() => onEdit({ palletsOpen: !draft.palletsOpen })} data-tour="receive-line-pallets" data-tour-key={line.id}>
                                {t('receipt_pallet_numbers')}
                            </button>
                        )}
                        {toInspect.map((pallet) => (
                            <Link key={pallet.palletId} href={`/inspection?palletId=${pallet.palletId}`} className={secondaryButtonClass} data-tour="receive-line-inspect" data-tour-key={line.id}>
                                {toInspect.length > 1 ? `${t('receipt_inspect')} ${pallet.reference}` : t('receipt_inspect')}
                            </Link>
                        ))}
                    </div>
                </td>
            </tr>
            {errors && (
                <tr className="block md:table-row">
                    <td colSpan={7} className="block px-2 pb-2 md:table-cell">
                        <ul id={errorId} role="alert" className="text-sm font-medium text-red-800">{errors.map((message) => <li key={message}>{message}</li>)}</ul>
                    </td>
                </tr>
            )}
            {draft.palletsOpen && receivable && (
                <tr className="block md:table-row">
                    <td colSpan={7} className="block px-2 pb-2 md:table-cell">
                        <fieldset id={`receive-pallets-${line.id}`} className="flex flex-wrap items-end gap-2 rounded border border-gray-200 bg-gray-50 p-2">
                            <legend className="sr-only">{t('receipt_pallet_numbers')}</legend>
                            <label className="text-xs text-gray-700">
                                <span className="block">{t('receipt_pallet_count')}</span>
                                <input inputMode="numeric" value={draft.palletCount} onChange={(event) => onEdit({ palletCount: event.target.value })} onKeyDown={onEnter}
                                    data-tour="receive-pallet-count" data-tour-key={line.id} className={`${inputClass} w-16 text-right`} />
                            </label>
                            {referenceInputs(draft).map((reference, index) => (
                                <label key={index} className="text-xs text-gray-700">
                                    <span className="block">{t('receipt_pallet_number', { index: String(index + 1) })}</span>
                                    {/* Scannen of typen; Enter gaat naar het volgende nummer, na het laatste wordt de regel ontvangen. */}
                                    <input value={reference} placeholder={t('receipt_pallet_number_auto')} autoComplete="off" className={`${inputClass} w-36`}
                                        data-tour="receive-pallet-number" data-tour-key={`${line.id}-${index}`}
                                        onChange={(event) => {
                                            const next = referenceInputs(draft);
                                            next[index] = event.target.value;
                                            onEdit({ references: next });
                                        }}
                                        onKeyDown={(event) => {
                                            if (event.key !== 'Enter') return;
                                            const following = event.currentTarget.closest('fieldset')?.querySelectorAll<HTMLInputElement>('[data-tour="receive-pallet-number"]')[index + 1];
                                            if (following) { event.preventDefault(); following.focus(); } else onEnter(event);
                                        }} />
                                </label>
                            ))}
                        </fieldset>
                    </td>
                </tr>
            )}
            {line.pallets.length > 0 && (
                <tr className="block md:table-row">
                    <td colSpan={7} className="block px-2 pb-2 md:table-cell">
                        <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-700" aria-label={t('receipt_received_pallets')}>
                            {line.pallets.map((pallet) => (
                                <li key={pallet.contentLineId} className="flex items-center gap-1.5">
                                    <span className="font-mono font-semibold text-gray-900">{pallet.reference}</span>
                                    <span>· {formatQuantity(pallet.quantity, locale)} · {formatKg(pallet.netWeightKg, locale)}</span>
                                    <QualityBadge status={pallet.qualityStatus} />
                                </li>
                            ))}
                        </ul>
                    </td>
                </tr>
            )}
        </tbody>
    );
}
