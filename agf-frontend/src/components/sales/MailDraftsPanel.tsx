'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime } from '../../lib/availability';
import { ApiError, toApiError } from '../../services/apiClient';
import { discardMailDraft, getMailStatus, getOrderMailDrafts, sendMailDraft, updateMailDraft } from '../../services/documentMailService';
import { getDocumentPdf } from '../../services/salesOrderDocumentService';
import type { MailDraft, MailStatus } from '../../types/documentMail';
import type { SalesOrder } from '../../types/salesOrder';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Te versturen (documenten deel 3): per document een mailconcept met ontvangers, onderwerp en tekst (aan te passen), een voorbeeld van de
// PDF, Versturen en Verwijderen. Er gaat nooit iets weg zonder een klik op Versturen. Zonder ingestelde afzender: PDF downloaden en tekst
// kopiëren. Verzonden: wanneer, naar wie en door wie.
export default function MailDraftsPanel({ order, reloadKey = 0 }: { order: SalesOrder; reloadKey?: number }) {
    const { t } = useTranslation();
    const [drafts, setDrafts] = useState<MailDraft[] | null>(null);
    const [status, setStatus] = useState<MailStatus | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [token, setToken] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setError(null);
        Promise.all([getOrderMailDrafts(order.id, controller.signal), getMailStatus(controller.signal)])
            .then(([found, mail]) => { setDrafts(found); setStatus(mail); })
            .catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [order.id, order.version, reloadKey, token]);

    // Na een orderwissel niets van de vorige order.
    const shown = drafts?.filter((d) => d.salesOrderId === order.id) ?? [];
    if (error === null && shown.length === 0) return null;
    return (
        <section aria-labelledby={`mail-${order.id}`} className="space-y-2 rounded-md border border-gray-300 bg-white px-4 py-3" data-testid="so-mail" data-tour="so-mail">
            <h3 id={`mail-${order.id}`} className="font-semibold text-gray-900">{t('mail_drafts_title')}</h3>
            {error && <ErrorNotice error={error} />}
            {status && !status.configured && <p role="status" className="rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm text-amber-950" data-testid="mail-not-configured">{t('mail_not_configured')}</p>}
            {status?.sender === 'Fake' && <p className="text-xs text-gray-600" data-testid="mail-fake-sender">{t('mail_sender_fake')}</p>}
            {shown.map((draft) => <MailDraftCard key={`${draft.id}-${draft.version}-${draft.status}-${draft.attempts}`} draft={draft} configured={status?.configured ?? false} onChanged={() => setToken((n) => n + 1)} />)}
        </section>
    );
}

type Confirm = { kind: 'many'; count: number } | { kind: 'resend' } | null;
// Bevestigingen die in deze verzendpoging al gegeven zijn: ze gaan samen mee, zodat de ene vraag de andere niet ongedaan maakt.
type Granted = { many: boolean; resend: boolean };
const NOTHING_GRANTED: Granted = { many: false, resend: false };

export function MailDraftCard({ draft, configured, onChanged }: { draft: MailDraft; configured: boolean; onChanged: () => void }) {
    const { t, locale } = useTranslation();
    const [to, setTo] = useState(draft.to ?? '');
    const [cc, setCc] = useState(draft.cc ?? '');
    const [subject, setSubject] = useState(draft.subject);
    const [body, setBody] = useState(draft.body);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [confirm, setConfirm] = useState<Confirm>(null);
    const [preview, setPreview] = useState<string | null>(null);
    const [copied, setCopied] = useState(false);
    // Wat er op de server staat (na een opslag in deze kaart): de versie voor de volgende aanroep en de basis voor "aangepast".
    const [saved, setSaved] = useState(() => ({ version: draft.version, to: draft.to ?? '', cc: draft.cc ?? '', subject: draft.subject, body: draft.body }));
    const [granted, setGranted] = useState<Granted>(NOTHING_GRANTED);
    const [conflict, setConflict] = useState<{ current: MailDraft | null } | null>(null);
    useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);
    const open = draft.status === 'Draft' || draft.status === 'Failed';
    const dirty = to !== saved.to || cc !== saved.cc || subject !== saved.subject || body !== saved.body;
    const id = (field: string) => `mail-${draft.id}-${field}`;
    const name = t(draft.documentKind === 'TransportOrder' ? 'so_transport_order_name' : 'so_slip_name', { number: draft.documentNumber, version: String(draft.documentVersion) });

    const run = async (work: () => Promise<void>) => {
        if (busy) return;
        setBusy(true);
        setError(null);
        try { await work(); } catch (caught) {
            const apiError = toApiError(caught);
            // Versieconflict: iemand anders heeft dit concept gewijzigd. Geen kale foutmelding, maar een verversactie.
            const code = (apiError.body as { code?: string } | null)?.code;
            if (apiError.kind === 'http' && apiError.status === 409 && (code === 'stale' || code === 'notOpen')) setConflict({ current: null });
            else setError(apiError);
        } finally { setBusy(false); }
    };
    // Actuele versie van de server ophalen. "Mijn wijzigingen behouden" neemt alleen de nieuwe versie over (de velden blijven zoals de
    // gebruiker ze heeft); anders worden de velden vervangen door de actuele tekst. Is het concept inmiddels verzonden of weg: lijst verversen.
    const fetchCurrent = async () => (await getOrderMailDrafts(draft.salesOrderId)).find((d) => d.id === draft.id);
    const refresh = (keepMine: boolean) => void run(async () => {
        const found = await fetchCurrent();
        if (!found || (found.status !== 'Draft' && found.status !== 'Failed')) { onChanged(); return; }
        setSaved({ version: found.version, to: found.to ?? '', cc: found.cc ?? '', subject: found.subject, body: found.body });
        if (!keepMine) { setTo(found.to ?? ''); setCc(found.cc ?? ''); setSubject(found.subject); setBody(found.body); }
        setConfirm(null);
        setGranted(NOTHING_GRANTED);
        setConflict(null);
    });
    // Toont de actuele serverversie bij de eigen wijzigingen, zodat de gebruiker kan vergelijken voordat hij kiest.
    const showCurrent = () => void run(async () => {
        const found = await fetchCurrent();
        if (!found) { onChanged(); return; }
        setConflict({ current: found });
    });
    // Aanpassingen opslaan (met de versie); de kaart onthoudt daarna wat er staat, zodat een bevestiging niet nog eens met de oude versie opslaat.
    const save = async () => {
        if (!dirty) return saved.version;
        const updated = await updateMailDraft(draft.id, { expectedVersion: saved.version, to, cc, subject, body });
        const next = { version: updated.version, to: updated.to ?? '', cc: updated.cc ?? '', subject: updated.subject, body: updated.body };
        setSaved(next);
        setTo(next.to);
        setCc(next.cc);
        setSubject(next.subject);
        setBody(next.body);
        return next.version;
    };
    // Eerst eventuele aanpassingen opslaan, dan versturen met de nieuwe versie. Een vraag om bevestiging (veel ontvangers, onbekende
    // uitkomst) komt als knop in de kaart; pas na die tweede klik gaat de mail weg, met alle bevestigingen van deze poging samen.
    const send = (allowed: Granted) => void run(async () => {
        const version = await save();
        setGranted(allowed);
        try {
            await sendMailDraft(draft.id, { expectedVersion: version, confirmManyRecipients: allowed.many, confirmResend: allowed.resend });
            setConfirm(null);
            onChanged();
        } catch (caught) {
            const apiError = toApiError(caught);
            const answer = apiError.body as { code?: string; count?: number } | null;
            if (answer?.code === 'confirmRecipients') { setConfirm({ kind: 'many', count: answer.count ?? 0 }); return; }
            if (answer?.code === 'uncertain') { setConfirm({ kind: 'resend' }); return; }
            // Uitkomst onzeker (verbinding weg, time-out, serverfout): de server kan de mail al hebben verwerkt. Haal het concept opnieuw
            // op, zodat de actuele status en versie (en de vraag over een onbekende uitkomst) getoond worden.
            if (apiError.kind !== 'http' || apiError.status === null || apiError.status >= 500) onChanged();
            throw apiError;
        }
    });
    // Een bevestiging hoort bij de ontvangers waarvoor ze gevraagd is: wijzigen ze, dan vervalt de vraag en is een nieuwe nodig.
    const changeRecipients = (set: (value: string) => void) => (value: string) => {
        set(value);
        setConfirm((current) => (current?.kind === 'many' ? null : current));
        setGranted((current) => ({ ...current, many: false }));
    };
    // Verwijderen gooit ook niet-opgeslagen aanpassingen weg: alleen de versie van wat er staat telt.
    const discard = () => void run(async () => {
        await discardMailDraft(draft.id, saved.version);
        onChanged();
    });
    const pdf = (download: boolean) => void run(async () => {
        const url = URL.createObjectURL(await getDocumentPdf(draft.salesOrderId, draft.documentId));
        if (!download) { if (preview) URL.revokeObjectURL(preview); setPreview(url); return; }
        const link = window.document.createElement('a');
        link.href = url;
        link.download = `${draft.documentNumber} v${draft.documentVersion}.pdf`;
        window.document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
    const copy = () => void run(async () => { await navigator.clipboard.writeText(`${subject}\n\n${body}`); setCopied(true); });

    return (
        <article className="space-y-2 rounded border border-gray-200 p-3" data-testid={`mail-draft-${draft.documentKind}`} data-tour={`so-mail-${draft.documentKind}`}>
            <div className="flex flex-wrap items-center gap-2">
                <span className="font-semibold">{name}</span>
                <span className="rounded bg-gray-100 px-2 py-0.5 text-xs" data-testid="mail-status">{t(`mail_status_${draft.status}`)}</span>
            </div>
            {draft.status === 'Sent' && (
                <p className="text-sm text-emerald-900" data-testid="mail-sent">{t('mail_sent_line', { date: draft.sentAt ? formatDateTime(draft.sentAt, locale) : '–', to: [draft.to, draft.cc].filter(Boolean).join(', '), name: draft.sentBy ?? '–' })}</p>
            )}
            {open && draft.previousSentAt && <p role="status" className="rounded border border-blue-200 bg-blue-50 px-2 py-1 text-sm text-blue-950">{t('mail_previous_sent', { date: formatDateTime(draft.previousSentAt, locale) })}</p>}
            {open && draft.missingRecipient && (
                <p role="alert" className="flex flex-wrap items-center gap-2 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm text-amber-950" data-testid="mail-missing">
                    {t('mail_missing_recipient', { name: draft.missingRecipient })}
                    <Link className="underline" href={draft.missingRelationId !== null ? `/relations?open=${draft.missingRelationId}&section=recipients` : '/settings#warehouse-recipients'} data-tour="so-mail-add-address">{t('mail_add_address')}</Link>
                </p>
            )}
            {draft.status === 'Failed' && draft.lastError && <p role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1 text-sm text-red-900" data-testid="mail-failed">{t('mail_failed', { reason: draft.lastError })}</p>}
            {draft.status === 'Sending' && <p role="status" className="text-sm text-gray-700">{t('mail_sending')}</p>}
            {error && <ErrorNotice error={error} />}
            {conflict && (
                <div role="alert" className="space-y-1 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm text-amber-950" data-testid="mail-conflict">
                    <p>{t('mail_conflict')}</p>
                    {conflict.current && (
                        <dl className="text-xs" data-testid="mail-conflict-current">
                            <dt className="font-medium">{t('mail_to')}</dt><dd>{[conflict.current.to, conflict.current.cc].filter(Boolean).join(', ') || '–'}</dd>
                            <dt className="font-medium">{t('mail_subject')}</dt><dd>{conflict.current.subject}</dd>
                            <dt className="font-medium">{t('mail_body')}</dt><dd className="whitespace-pre-wrap">{conflict.current.body}</dd>
                        </dl>
                    )}
                    <div className="flex flex-wrap gap-2">
                        {!conflict.current && <button type="button" className={secondaryButtonClass} disabled={busy} onClick={showCurrent} data-testid="mail-conflict-compare">{t('mail_conflict_compare')}</button>}
                        <button type="button" className={primaryButtonClass} disabled={busy} onClick={() => refresh(false)} data-testid="mail-conflict-reload">{t('mail_conflict_reload')}</button>
                        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => refresh(true)} data-testid="mail-conflict-keep">{t('mail_conflict_keep')}</button>
                    </div>
                </div>
            )}
            {open && (
                <fieldset disabled={busy} className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                    <div>
                        <label htmlFor={id('to')} className="text-xs font-medium text-gray-700">{t('mail_to')}</label>
                        <input id={id('to')} value={to} onChange={(event) => changeRecipients(setTo)(event.target.value)} className={inputClass} data-tour="so-mail-to" />
                    </div>
                    <div>
                        <label htmlFor={id('cc')} className="text-xs font-medium text-gray-700">{t('mail_cc')}</label>
                        <input id={id('cc')} value={cc} onChange={(event) => changeRecipients(setCc)(event.target.value)} className={inputClass} />
                    </div>
                    <div className="sm:col-span-2">
                        <label htmlFor={id('subject')} className="text-xs font-medium text-gray-700">{t('mail_subject')}</label>
                        <input id={id('subject')} value={subject} maxLength={300} onChange={(event) => setSubject(event.target.value)} className={inputClass} />
                    </div>
                    <div className="sm:col-span-2">
                        <label htmlFor={id('body')} className="text-xs font-medium text-gray-700">{t('mail_body')}</label>
                        <textarea id={id('body')} rows={8} maxLength={10000} value={body} onChange={(event) => setBody(event.target.value)} className={inputClass} />
                    </div>
                </fieldset>
            )}
            <p className="text-xs text-gray-600">{t('mail_attachment', { name })}</p>
            {confirm && (
                <p role="alert" className="flex flex-wrap items-center gap-2 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm text-amber-950" data-testid="mail-confirm">
                    {confirm.kind === 'many' ? t('mail_confirm_many', { count: String(confirm.count) }) : t('mail_confirm_resend')}
                    <button type="button" className={primaryButtonClass} disabled={busy} onClick={() => send({ ...granted, [confirm.kind]: true })} data-tour="so-mail-confirm">{t('mail_send_anyway')}</button>
                    <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setConfirm(null)}>{t('cancel')}</button>
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                {(open || draft.status === 'Sending') && configured && <button type="button" className={primaryButtonClass} disabled={busy || !to.trim()} onClick={() => send(NOTHING_GRANTED)} data-tour="so-mail-send">{busy ? t('supply_saving') : t('mail_send')}</button>}
                <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => pdf(false)} data-tour="so-mail-preview">{t('mail_preview')}</button>
                {!configured && open && <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => pdf(true)} data-testid="mail-download">{t('so_slip_download')}</button>}
                {!configured && open && <button type="button" className={secondaryButtonClass} disabled={busy} onClick={copy} data-testid="mail-copy">{copied ? t('mail_copied') : t('mail_copy')}</button>}
                {open && <button type="button" className={secondaryButtonClass} disabled={busy} onClick={discard}>{t('mail_discard')}</button>}
            </div>
            {preview && <iframe src={preview} title={name} className="h-96 w-full rounded border border-gray-200" data-testid="mail-preview" />}
        </article>
    );
}
