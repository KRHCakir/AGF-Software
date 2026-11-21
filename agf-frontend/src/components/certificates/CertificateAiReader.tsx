'use client';

import { useId, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type AiDraft, approvalRows, draftFromProposal, fieldMark, linkedProductIds, problemCodes } from '../../lib/certificateAiProposal';
import { certificateStatus, daysLeft } from '../../lib/certificateRules';
import { todayInAmsterdam } from '../../lib/calendarDate';
import { closeCertificateExtraction, getCertificateAiDocument, getCertificateAiStatus, readCertificateDocument } from '../../services/certificateService';
import { type ApiError, toApiError } from '../../services/apiClient';
import { CUSTOM_TYPE, GGN_TYPE } from '../../types/certificate';
import type { AiCertificateExtraction } from '../../types/certificateAi';
import AiDocumentViewer from '../purchase/AiDocumentViewer';
import AiUploadPanel from '../purchase/AiUploadPanel';
import DateField from '../shared/DateField';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import CertificateStatusBadge from './CertificateStatusBadge';
import type { CertificateOwner, useCertificates } from './useCertificates';

type Certificates = ReturnType<typeof useCertificates>;

// Certificaten deel 2: in het blok Certificaten een PDF of foto laten lezen, naast handmatig invullen (dat blijft precies zoals het was).
// Het voorstel staat in het bekende tweedelige scherm: het document links, het voorstel rechts, met gele markering bij twijfel en "niet
// gevonden" waar iets ontbreekt. Per product de letterlijke tekst uit het document (met de pagina, ook uit een bijlage) en de voorgestelde
// koppeling met een eigen product; niet te koppelen producten blijven zichtbaar als "niet gekoppeld" en de gebruiker kiest zelf via het
// zoekveld of laat ze open. Nooit een nieuw product. Goedkeuren slaat op via het gewone opslaan (met de goedkeuring en het document als
// nieuwe versie); Handmatig verder zet de waarden als gewone regel in het blok; Afwijzen sluit het voorstel zonder iets op te slaan.
export default function CertificateAiReader({ owner, certificates, blockedReason, dataTour }: {
    owner: CertificateOwner;
    certificates: Certificates;
    blockedReason: string | null;
    dataTour: string;
}) {
    const { t } = useTranslation();
    const [extraction, setExtraction] = useState<AiCertificateExtraction | null>(null);
    const [draft, setDraft] = useState<AiDraft | null>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [problems, setProblems] = useState<Record<string, string[]>>({});
    const [notice, setNotice] = useState<string | null>(null);
    const file = useRef<File | null>(null);
    const listId = useId();
    const relationId = owner.kind === 'own' ? null : owner.id;
    const reference = certificates.reference;

    const start = (read: AiCertificateExtraction) => {
        setNotice(null);
        setError(null);
        setProblems({});
        setExtraction(read);
        setDraft(read.proposal === null ? null : draftFromProposal(read.proposal));
    };
    const done = (message: string | null) => { setExtraction(null); setDraft(null); setNotice(message); setProblems({}); setError(null); };

    const approve = async () => {
        if (extraction === null || draft === null || busy) return;
        setBusy(true);
        setError(null);
        const { rows, index } = approvalRows(certificates.stored, draft);
        const result = await certificates.approveAi(rows, index, extraction.id);
        setBusy(false);
        if (result.ok) { done(t('cert_ai_approved')); return; }
        const found: Record<string, string[]> = {};
        // Alleen de regel van het voorstel telt hier; fouten in andere regels kunnen niet (die zijn al opgeslagen).
        for (const problem of result.problems) {
            const field = problem.field.split('.').pop() ?? '';
            (found[field] ??= []).push(t(`cert_error_${problem.code}`));
        }
        setProblems(found);
        setError(result.error);
    };

    const close = async (outcome: 'Rejected' | 'Manual') => {
        if (extraction === null || busy) return;
        setBusy(true);
        try {
            await closeCertificateExtraction(extraction.id, outcome);
        } catch (caught) {
            setError(toApiError(caught));
            setBusy(false);
            return;
        }
        setBusy(false);
        if (outcome === 'Manual' && draft !== null) {
            // Handmatig verder: de waarden als gewone regel in het blok en het document als openstaand bestand bij die regel (ook bij een
            // bestaand certificaat). Alles gaat pas mee bij het gewone opslaan, met de foutafhandeling daarvan; niet opslaan = niets opgeslagen.
            const { rows, index } = approvalRows(certificates.rows, draft);
            const pending = file.current !== null ? [file.current] : [];
            certificates.setRows(rows.map((r, i) => (i === index ? { ...r, pendingFiles: [...r.pendingFiles, ...pending] } : r)));
            done(t('cert_ai_manual_done'));
            return;
        }
        done(t('cert_ai_rejected'));
    };

    const edit = (patch: Partial<AiDraft['row']>) => setDraft((current) => (current === null ? current : { ...current, row: { ...current.row, ...patch } }));
    const link = (index: number, text: string) => setDraft((current) => {
        if (current === null) return current;
        const product = certificates.products.find((p) => p.name.toLowerCase() === text.trim().toLowerCase());
        return { ...current, links: current.links.map((l, i) => (i === index ? { ...l, productId: text.trim() === '' ? null : product?.id ?? l.productId, suggested: false } : l)) };
    });
    const unlink = (index: number) => setDraft((current) => (current === null ? current : { ...current, links: current.links.map((l, i) => (i === index ? { ...l, productId: null, suggested: false } : l)) }));

    const mark = (field: string) => (extraction === null ? null : fieldMark(extraction, field));
    const ring = (field: string) => (mark(field) === 'doubt' ? 'ring-2 ring-amber-400 bg-amber-50' : '');
    const markText = (field: string) => {
        const m = mark(field);
        if (m === null || extraction === null) return null;
        const codes = problemCodes(extraction, field);
        const key = m === 'missing' ? 'cert_ai_missing' : codes.includes('ggnInvalid') ? 'cert_ai_ggn_invalid' : codes.includes('ggnCheckDigit') ? 'cert_warning_ggn_check_digit'
            : codes.includes('invalidDate') ? 'cert_ai_invalid_date' : 'cert_ai_doubt';
        return <span className={`block text-xs ${m === 'missing' ? 'text-gray-700' : 'font-medium text-amber-900'}`} data-testid={`cert-ai-mark-${field}`}>{t(key)}</span>;
    };
    const today = todayInAmsterdam();
    const proposal = extraction?.proposal ?? null;

    return (
        <div className="space-y-2" data-tour={`${dataTour}-ai`}>
            {extraction === null && (
                <AiUploadPanel<AiCertificateExtraction> loadStatus={getCertificateAiStatus} labelKey="cert_ai_upload" tour={`${dataTour}-ai`} readingKey="cert_ai_reading"
                    errorKeyOf={(code) => (['AI_OFF', 'AI_NOT_CONFIGURED', 'AI_UNAVAILABLE', 'AI_INVALID', 'AI_UNSUPPORTED', 'AI_FAILED', 'AI_NETWORK', 'file_encrypted'].includes(code) ? `cert_ai_error_${code}` : `ai_error_${code}`)}
                    disabledReason={blockedReason} read={(chosen) => { file.current = chosen; return readCertificateDocument(chosen, relationId); }} onProposal={start} />
            )}
            {notice && <p role="status" className="text-sm text-green-900" data-testid="cert-ai-notice">{notice}</p>}
            {extraction !== null && (
                <section className="grid grid-cols-1 gap-3 rounded-md border border-blue-200 bg-white p-2 lg:grid-cols-2" aria-labelledby={`${listId}-title`} data-testid="cert-ai-review" data-tour={`${dataTour}-ai-review`}>
                    <AiDocumentViewer extraction={extraction} load={getCertificateAiDocument} dataTour={`${dataTour}-ai-document`} />
                    <div className="min-w-0 space-y-3">
                        <div>
                            <h3 id={`${listId}-title`} className="text-sm font-semibold text-gray-900">{t('cert_ai_title')}</h3>
                            <p className="text-xs text-gray-700">{t('cert_ai_by', { model: extraction.modelName ?? extraction.model ?? '' })}</p>
                        </div>
                        {proposal === null || draft === null || reference === null ? <p className="text-sm text-gray-700">{t('cert_ai_no_proposal')}</p> : (
                            <>
                                {proposal.holderMismatch && (
                                    <p role="alert" className="rounded border border-amber-400 bg-amber-50 px-2 py-1 text-sm font-medium text-amber-950" data-testid="cert-ai-holder-mismatch">
                                        ⚠ {t('cert_ai_holder_mismatch', { name: proposal.holderMismatch })}
                                    </p>
                                )}
                                {proposal.sameNumberCertificateId !== null && certificates.stored.some((r) => r.id === proposal.sameNumberCertificateId) && (
                                    <fieldset className="rounded border border-blue-200 bg-blue-50 p-2 text-sm" data-testid="cert-ai-same-number">
                                        <legend className="px-1 text-xs font-medium text-blue-950">{t('cert_ai_same_number', { type: proposal.sameNumberTypeName ?? '', number: proposal.number ?? '' })}</legend>
                                        <label className="flex items-center gap-2"><input type="radio" name={`${listId}-target`} checked={draft.targetId !== null}
                                            onChange={() => setDraft({ ...draft, targetId: proposal.sameNumberCertificateId })} /> {t('cert_ai_new_version')}</label>
                                        <label className="flex items-center gap-2"><input type="radio" name={`${listId}-target`} checked={draft.targetId === null}
                                            onChange={() => setDraft({ ...draft, targetId: null })} /> {t('cert_ai_second_certificate')}</label>
                                    </fieldset>
                                )}
                                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                                    <div>
                                        <label htmlFor={`${listId}-type-input`} className="text-xs font-medium text-gray-700">{t('cert_type')} *</label>
                                        <select {...fieldProps(`${listId}-type`, problems.typeCode)} value={draft.row.typeCode} className={`${inputClass} ${ring('typeCode')}`}
                                            onChange={(event) => edit({ typeCode: event.target.value })} data-testid="cert-ai-type">
                                            <option value="">{t('cert_type_choose')}</option>
                                            {reference.types.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
                                            <option value={CUSTOM_TYPE}>{t('cert_type_custom_new')}</option>
                                        </select>
                                        {markText('typeCode')}
                                        {proposal.documentTypeName && <span className="block text-xs text-gray-600">{t('cert_ai_in_document', { text: proposal.documentTypeName })}</span>}
                                        <FieldError id={`${listId}-type`} messages={problems.typeCode} />
                                    </div>
                                    {draft.row.typeCode === CUSTOM_TYPE && (
                                        <div>
                                            <label htmlFor={`${listId}-custom-input`} className="text-xs font-medium text-gray-700">{t('cert_custom_name')} *</label>
                                            <input {...fieldProps(`${listId}-custom`, problems.customTypeName)} value={draft.row.customTypeName} maxLength={80} className={inputClass}
                                                onChange={(event) => edit({ customTypeName: event.target.value })} />
                                            <FieldError id={`${listId}-custom`} messages={problems.customTypeName} />
                                        </div>
                                    )}
                                    <div>
                                        <label htmlFor={`${listId}-number-input`} className="text-xs font-medium text-gray-700">{draft.row.typeCode === GGN_TYPE ? t('cert_number_ggn') : t('cert_number')} *</label>
                                        <input {...fieldProps(`${listId}-number`, problems.number)} value={draft.row.number} maxLength={100} inputMode={draft.row.typeCode === GGN_TYPE ? 'numeric' : undefined}
                                            className={`${inputClass} font-mono ${ring('number')}`} onChange={(event) => edit({ number: event.target.value })} data-testid="cert-ai-number" />
                                        {markText('number')}
                                        <FieldError id={`${listId}-number`} messages={problems.number} />
                                    </div>
                                    <div>
                                        <label htmlFor={`${listId}-holder-input`} className="text-xs font-medium text-gray-700">{t('cert_ai_holder')}</label>
                                        <input id={`${listId}-holder-input`} value={draft.row.producerName} maxLength={200} className={`${inputClass} ${ring('holderName')}`}
                                            onChange={(event) => edit({ producerName: event.target.value })} data-testid="cert-ai-holder" />
                                        {proposal.holderCountry && <span className="block text-xs text-gray-600">{t('cert_ai_country', { country: proposal.holderCountry })}</span>}
                                        {mark('holderName') === 'missing' && markText('holderName')}
                                    </div>
                                    <div>
                                        <label htmlFor={`${listId}-body-input`} className="text-xs font-medium text-gray-700">{t('cert_body')}</label>
                                        <input id={`${listId}-body-input`} value={draft.row.certificationBody} maxLength={200} className={`${inputClass} ${ring('certificationBody')}`}
                                            onChange={(event) => edit({ certificationBody: event.target.value })} />
                                        {markText('certificationBody')}
                                    </div>
                                    <div>
                                        <label htmlFor={`${listId}-from-input`} className="text-xs font-medium text-gray-700">{t('cert_valid_from')}</label>
                                        <DateField {...fieldProps(`${listId}-from`, problems.validFrom)} value={draft.row.validFrom} onChange={(value) => edit({ validFrom: value })} />
                                        {markText('validFrom')}
                                        <FieldError id={`${listId}-from`} messages={problems.validFrom} />
                                    </div>
                                    <div>
                                        <label htmlFor={`${listId}-until-input`} className="text-xs font-medium text-gray-700">{t('cert_valid_until')}</label>
                                        <div className={`rounded ${ring('validUntil')}`}>
                                            <DateField {...fieldProps(`${listId}-until`, problems.validUntil)} value={draft.row.validUntil} onChange={(value) => edit({ validUntil: value })} />
                                        </div>
                                        {markText('validUntil')}
                                        <FieldError id={`${listId}-until`} messages={problems.validUntil} />
                                        <CertificateStatusBadge status={certificateStatus(draft.row.validUntil, today, certificates.warningDays)} daysLeft={daysLeft(draft.row.validUntil, today)} testId="cert-ai-status" />
                                    </div>
                                </div>
                                <div>
                                    <span className="text-xs font-medium text-gray-700" id={`${listId}-products`}>{t('cert_ai_products')}</span>
                                    {mark('products') === 'missing' && markText('products')}
                                    <datalist id={`${listId}-product-list`}>{certificates.products.map((p) => <option key={p.id} value={p.name} />)}</datalist>
                                    <ul className="mt-1 space-y-1" aria-labelledby={`${listId}-products`} data-testid="cert-ai-products">
                                        {draft.links.map((l, i) => {
                                            const linked = certificates.products.find((p) => p.id === l.productId);
                                            return (
                                                <li key={`${l.text}-${i}`} className={`flex flex-wrap items-center gap-2 rounded px-1 py-0.5 text-sm ${linked ? '' : 'bg-amber-50'}`} data-testid={`cert-ai-product-${i}`}>
                                                    <span className="min-w-0 flex-1 break-words font-medium text-gray-900">“{l.text}”{l.page !== null && <span className="ml-1 text-xs font-normal text-gray-600">{t('cert_ai_page', { page: String(l.page) })}</span>}</span>
                                                    {linked ? (
                                                        <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-0.5 text-xs text-emerald-900 ring-1 ring-emerald-200">
                                                            {linked.name}{l.suggested ? ` · ${t('cert_ai_suggested')}` : ''}
                                                            <button type="button" aria-label={t('cert_ai_unlink', { name: linked.name })} onClick={() => unlink(i)}>×</button>
                                                        </span>
                                                    ) : (
                                                        <>
                                                            <span className="text-xs font-medium text-amber-900">{t('cert_ai_unlinked')}</span>
                                                            <label htmlFor={`${listId}-link-${i}`} className="sr-only">{t('cert_ai_link_label', { text: l.text })}</label>
                                                            <input id={`${listId}-link-${i}`} type="search" list={`${listId}-product-list`} placeholder={t('cert_ai_link_placeholder')}
                                                                className={`${inputClass} !mt-0 w-44`} onChange={(event) => link(i, event.target.value)} data-testid={`cert-ai-link-${i}`} />
                                                        </>
                                                    )}
                                                </li>
                                            );
                                        })}
                                    </ul>
                                    <span className="block text-xs text-gray-600">{t('cert_ai_products_hint', { count: String(linkedProductIds(draft.links).length) })}</span>
                                </div>
                                {proposal.sites.length > 0 && (
                                    <div className="text-xs text-gray-700" data-testid="cert-ai-sites">
                                        <span className="font-medium">{t('cert_ai_sites')}</span>{' '}
                                        {proposal.sites.map((s) => (s.country ? `${s.name} (${s.country})` : s.name)).join(', ')}
                                    </div>
                                )}
                                {error && <ErrorNotice error={error} />}
                                <div className="flex flex-wrap items-center gap-2">
                                    <button type="button" className={primaryButtonClass} disabled={busy || !certificates.canApproveAi} onClick={() => void approve()} data-testid="cert-ai-approve" data-tour={`${dataTour}-ai-approve`}>
                                        {t('cert_ai_approve')}
                                    </button>
                                    <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void close('Manual')} data-testid="cert-ai-manual">{t('ai_manual')}</button>
                                    <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void close('Rejected')} data-testid="cert-ai-reject">{t('ai_reject')}</button>
                                </div>
                                <p className="text-xs text-gray-600">{t('cert_ai_approve_hint')}</p>
                            </>
                        )}
                        {(proposal === null || draft === null) && error && <ErrorNotice error={error} />}
                        {(proposal === null || draft === null) && (
                            <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void close('Rejected')} data-testid="cert-ai-reject">{t('ai_reject')}</button>
                        )}
                    </div>
                </section>
            )}
        </div>
    );
}
