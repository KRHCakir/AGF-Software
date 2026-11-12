'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { ApiError, toApiError } from '../../services/apiClient';
import { confirmSalesOrder, getDocumentPdf, getSalesOrderDocuments, markDocumentSent, updateSalesOrderLoading } from '../../services/salesOrderDocumentService';
import type { SalesOrder } from '../../types/salesOrder';
import type { SalesOrderDocument, SalesOrderDocuments as Documents } from '../../types/salesOrderDocument';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Bevestigen en documenten van een verkooporder (documenten deel 1). Bevestigen maakt de laadbon voor de loods (geen prijzen); verandert de
// order daarna, dan komt er een nieuwe versie en staat er "Laadbon gewijzigd, opnieuw versturen" tot hij is doorgegeven. Er wordt niets
// verstuurd: dat doet deel 3. Bekijken, Downloaden en Afdrukken halen de PDF van precies die versie op.

const MAX_PLATE = 32;
const CLEANUP_MS = 60_000;

// "2026-11-13T06:30" (datetime-local, tijd van de browser) naar een UTC-tijdstip; leeg = de leverdatum zonder tijd.
export function loadAtFromInput(value: string): string | null {
    if (value.trim() === '') return null;
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

export function inputFromLoadAt(value: string | null): string {
    if (value === null) return '';
    const date = new Date(value);
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

// Bekijken opent het venster meteen bij de klik (anders blokkeert de browser het als pop-up) en zet de PDF erin zodra die er is; een
// geblokkeerd venster geeft een melding. Downloaden en Afdrukken hebben geen nieuw venster nodig.
async function openPdf(orderId: number, document: SalesOrderDocument, mode: 'view' | 'download' | 'print') {
    const target = mode === 'view' ? window.open('', '_blank') : null;
    if (mode === 'view' && target === null) throw new Error('popup-blocked');
    let url: string;
    try {
        url = URL.createObjectURL(await getDocumentPdf(orderId, document.id));
    } catch (caught) {
        target?.close();
        throw caught;
    }
    // De URL en een eventueel afdrukframe worden na een minuut opgeruimd: de browser heeft de PDF dan al geladen.
    let frame: HTMLIFrameElement | null = null;
    setTimeout(() => { URL.revokeObjectURL(url); frame?.remove(); }, CLEANUP_MS);
    if (mode === 'view') {
        target!.location.href = url;
    } else if (mode === 'download') {
        const link = window.document.createElement('a');
        link.href = url;
        link.download = `Laadbon ${document.number} v${document.version}.pdf`;
        // Kort aan de DOM toevoegen: in o.a. Safari wordt een klik op een losstaand element niet uitgevoerd.
        window.document.body.appendChild(link);
        link.click();
        link.remove();
    } else {
        // Afdrukken: de PDF in een onzichtbaar frame laden en de afdrukdialoog van de browser openen.
        const printFrame = window.document.createElement('iframe');
        printFrame.style.position = 'fixed';
        printFrame.style.width = '0';
        printFrame.style.height = '0';
        printFrame.style.border = '0';
        printFrame.src = url;
        printFrame.onload = () => printFrame.contentWindow?.print();
        window.document.body.appendChild(printFrame);
        frame = printFrame;
    }
}

// onChanged: de order zelf veranderde (bevestigd, of laden op en kenteken): de pagina haalt de order opnieuw op, met de nieuwe versie.
export default function SalesOrderDocuments({ order, onChanged }: { order: SalesOrder; onChanged?: () => void }) {
    const { t, locale } = useTranslation();
    const [loaded, setLoaded] = useState<{ documents: Documents; version: number } | null>(null);
    // Alleen gegevens van de getoonde order en precies de getoonde orderversie: na een orderwissel of -wijziging is de laadbon tot een
    // geslaagde verversing onbeschikbaar (anders zou een verouderde bon bruikbaar of doorgeefbaar blijven).
    const data = loaded !== null && loaded.documents.salesOrderId === order.id && loaded.version === order.version ? loaded.documents : null;
    const setData = (documents: Documents) => setLoaded({ documents, version: order.version });
    const [reloadToken, setReloadToken] = useState(0);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);
    const [asking, setAsking] = useState(false);
    const [editing, setEditing] = useState(false);
    const [loadAt, setLoadAt] = useState('');
    const [plate, setPlate] = useState('');
    const [showOlder, setShowOlder] = useState(false);
    const [popupBlocked, setPopupBlocked] = useState(false);
    const cancelled = order.status === 'Cancelled';

    // Een andere order: formulierstatus en meldingen van de vorige order wissen.
    useEffect(() => {
        setLoaded(null);
        setLoadError(null);
        setError(null);
        setPopupBlocked(false);
        setAsking(false);
        setEditing(false);
        setLoadAt('');
        setPlate('');
        setShowOlder(false);
    }, [order.id]);

    // Opnieuw ophalen na elke wijziging van de order (versie): een nieuwe reservering kan een nieuwe versie van de laadbon geven.
    useEffect(() => {
        const controller = new AbortController();
        const version = order.version;
        setLoadError(null);
        getSalesOrderDocuments(order.id, controller.signal)
            .then((found) => { setLoaded({ documents: found, version }); setLoadError(null); })
            .catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, [order.id, order.version, reloadToken]);

    const run = async (call: () => Promise<Documents>, after?: () => void) => {
        if (busy) return;
        setBusy(true);
        setError(null);
        try {
            setData(await call());
            after?.();
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };
    const pdf = (document: SalesOrderDocument, mode: 'view' | 'download' | 'print') => {
        setError(null);
        setPopupBlocked(false);
        openPdf(order.id, document, mode).catch((caught) => {
            if (caught instanceof Error && caught.message === 'popup-blocked') setPopupBlocked(true);
            else setError(toApiError(caught));
        });
    };
    const details = () => ({ loadAt: loadAtFromInput(loadAt), licensePlate: plate.trim() === '' ? null : plate.trim() });
    const startEdit = () => {
        setLoadAt(inputFromLoadAt(data?.loadAt ?? null));
        setPlate(data?.licensePlate ?? '');
        setEditing(true);
    };
    const confirm = (event: FormEvent) => {
        event.preventDefault();
        void run(() => confirmSalesOrder(order.id, details()), () => { setAsking(false); onChanged?.(); });
    };
    const saveLoading = (event: FormEvent) => {
        event.preventDefault();
        void run(() => updateSalesOrderLoading(order.id, details()), () => { setEditing(false); onChanged?.(); });
    };

    const slips = (data?.documents ?? []).filter((document) => document.kind === 'LoadingSlip');
    const latest = slips.find((document) => document.latest) ?? null;
    const older = slips.filter((document) => !document.latest);
    const delivery = formatCalendarDate(order.requestedDeliveryDate, locale);

    const loadingFields = (
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <div>
                <label htmlFor={`load-at-${order.id}`} className="text-xs font-medium text-gray-700">{t('so_load_at')}</label>
                <input id={`load-at-${order.id}`} type="datetime-local" value={loadAt} onChange={(event) => setLoadAt(event.target.value)} className={inputClass} data-tour="so-load-at" />
                <span className="block text-xs text-gray-600">{t('so_load_at_hint', { date: delivery })}</span>
            </div>
            <div>
                <label htmlFor={`plate-${order.id}`} className="text-xs font-medium text-gray-700">{t('so_license_plate')}</label>
                <input id={`plate-${order.id}`} value={plate} maxLength={MAX_PLATE} onChange={(event) => setPlate(event.target.value)} className={inputClass} data-tour="so-license-plate" />
                <span className="block text-xs text-gray-600">{t('so_license_plate_hint')}</span>
            </div>
        </div>
    );

    return (
        <section aria-labelledby={`documents-${order.id}`} className="space-y-2 rounded-md border border-gray-300 bg-white px-4 py-3" data-testid="so-documents" data-tour="so-documents">
            <div className="flex flex-wrap items-center gap-2">
                <h3 id={`documents-${order.id}`} className="font-semibold text-gray-900">{t('so_documents')}</h3>
                {data?.confirmedAt && <span className="rounded border border-emerald-300 bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-900" data-testid="so-confirmed">{t('so_confirmed_badge')}</span>}
            </div>
            {loadError && data === null && (
                <ErrorNotice error={loadError} actions={<button type="button" className={secondaryButtonClass} onClick={() => setReloadToken((n) => n + 1)} data-testid="so-documents-retry">{t('availability_retry')}</button>} />
            )}
            {error && <ErrorNotice error={error} />}
            {popupBlocked && <p role="alert" className="rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm text-amber-950" data-testid="so-slip-popup-blocked">{t('so_slip_popup_blocked')}</p>}
            {/* Geannuleerd: niet laden, ook als de order nooit bevestigd was. */}
            {cancelled && <p role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1 text-sm text-red-900" data-testid="so-slip-cancelled">{t('so_slip_cancelled')}</p>}

            {data && data.confirmedAt === null && !cancelled && (
                order.lines.length === 0
                    ? <p className="text-sm text-gray-700" data-testid="so-confirm-no-lines">{t('so_confirm_no_lines')}</p>
                    : asking
                        ? (
                            <form onSubmit={confirm} className="space-y-2 rounded border border-blue-200 bg-blue-50 px-3 py-2" data-testid="so-confirm-question">
                                <p className="text-sm text-blue-950">{t('so_confirm_text')}</p>
                                {loadingFields}
                                <div className="flex flex-wrap gap-2">
                                    <button type="submit" className={primaryButtonClass} disabled={busy} data-tour="so-confirm-yes">{busy ? t('supply_saving') : t('so_confirm_yes')}</button>
                                    <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setAsking(false)}>{t('cancel')}</button>
                                </div>
                            </form>
                        )
                        : (
                            <div className="flex flex-wrap items-center gap-2">
                                <p className="text-sm text-gray-700">{t('so_not_confirmed')}</p>
                                <button type="button" className={primaryButtonClass} data-tour="so-confirm" onClick={() => { setLoadAt(''); setPlate(''); setAsking(true); }}>{t('so_confirm')}</button>
                            </div>
                        )
            )}

            {data?.confirmedAt && (
                <>
                    <p className="text-sm text-gray-800" data-testid="so-confirmed-by">
                        {t('so_confirmed_by', { date: formatDateTime(data.confirmedAt, locale), name: data.confirmedBy ?? '–' })}
                        {' · '}{t('so_load_at')}: {data.loadAt ? formatDateTime(data.loadAt, locale) : delivery}
                        {' · '}{t('so_license_plate')}: {data.licensePlate ?? t('so_license_plate_empty')}
                        {!cancelled && !editing && <button type="button" className="ml-2 text-blue-800 underline underline-offset-2" onClick={startEdit} data-tour="so-loading-edit">{t('so_loading_edit')}</button>}
                    </p>
                    {editing && (
                        <form onSubmit={saveLoading} className="space-y-2 rounded border border-gray-200 bg-gray-50 px-3 py-2" data-testid="so-loading-form">
                            {loadingFields}
                            <div className="flex flex-wrap gap-2">
                                <button type="submit" className={primaryButtonClass} disabled={busy}>{busy ? t('supply_saving') : t('so_loading_save')}</button>
                                <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setEditing(false)}>{t('cancel')}</button>
                            </div>
                        </form>
                    )}
                    {data.needsResend && latest && !cancelled && (
                        <p role="alert" className="flex flex-wrap items-center gap-2 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm font-medium text-amber-950" data-testid="so-slip-changed">
                            ⚠ {t('so_slip_changed', { version: String(latest.version) })}
                            <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void run(() => markDocumentSent(order.id, latest.id))} data-tour="so-slip-sent">{t('so_slip_sent')}</button>
                        </p>
                    )}
                    {latest && (
                        <div className="flex flex-wrap items-center gap-2 text-sm" data-testid="so-slip-latest" data-tour="so-slip">
                            <span className="font-semibold">{t('so_slip_name', { number: latest.number, version: String(latest.version) })}</span>
                            <span className="text-gray-600">{formatDateTime(latest.createdAt, locale)} · {latest.createdBy}</span>
                            {latest.sentAt && <span className="text-xs text-gray-600">{t('so_slip_sent_by', { date: formatDateTime(latest.sentAt, locale), name: latest.sentBy ?? '–' })}</span>}
                            <span className="ml-auto flex flex-wrap gap-2">
                                <button type="button" className={secondaryButtonClass} onClick={() => pdf(latest, 'view')} data-tour="so-slip-view">{t('so_slip_view')}</button>
                                <button type="button" className={secondaryButtonClass} onClick={() => pdf(latest, 'download')} data-tour="so-slip-download">{t('so_slip_download')}</button>
                                <button type="button" className={secondaryButtonClass} onClick={() => pdf(latest, 'print')} data-tour="so-slip-print">{t('so_slip_print')}</button>
                            </span>
                        </div>
                    )}
                    {older.length > 0 && (
                        <div className="text-sm">
                            <button type="button" className="text-blue-800 underline underline-offset-2" aria-expanded={showOlder} onClick={() => setShowOlder((open) => !open)}>
                                {t(showOlder ? 'so_slip_older_hide' : 'so_slip_older', { count: String(older.length) })}
                            </button>
                            {showOlder && (
                                <ul className="mt-1 space-y-1" data-testid="so-slip-older">
                                    {older.map((document) => (
                                        <li key={document.id} className="flex flex-wrap items-center gap-2 text-gray-700">
                                            <span>{t('so_slip_name', { number: document.number, version: String(document.version) })} · {formatDateTime(document.createdAt, locale)}</span>
                                            <button type="button" className="text-blue-800 underline underline-offset-2" onClick={() => pdf(document, 'view')}>{t('so_slip_view')}</button>
                                            <button type="button" className="text-blue-800 underline underline-offset-2" onClick={() => pdf(document, 'download')}>{t('so_slip_download')}</button>
                                        </li>
                                    ))}
                                </ul>
                            )}
                        </div>
                    )}
                </>
            )}
        </section>
    );
}
