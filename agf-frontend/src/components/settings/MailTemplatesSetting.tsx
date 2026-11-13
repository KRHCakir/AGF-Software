'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getMailTemplates, saveMailTemplate } from '../../services/documentMailService';
import type { MailTemplate, MailTemplates } from '../../types/documentMail';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// De sjablonen van het mailconcept per document (documenten deel 3, Beheer): onderwerp en tekst met invulvelden, platte tekst, geen
// opmaakeditor. "Standaardtekst" zet het vaste sjabloon terug. Geldt voor nieuwe concepten.
export default function MailTemplatesSetting() {
    const { t } = useTranslation();
    const [value, setValue] = useState<MailTemplates | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState<string | null>(null);
    const [saved, setSaved] = useState<string | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        getMailTemplates(controller.signal).then(setValue).catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const change = (kind: string, patch: Partial<MailTemplate>) => {
        if (!value) return;
        setValue({ ...value, templates: value.templates.map((template) => (template.kind === kind ? { ...template, ...patch } : template)) });
        setSaved(null);
    };
    const save = async (template: Pick<MailTemplate, 'kind' | 'subject' | 'body'>) => {
        setBusy(template.kind);
        setError(null);
        try {
            setValue(await saveMailTemplate(template));
            setSaved(template.kind);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(null);
        }
    };

    return (
        <section aria-labelledby="mail-templates" className="space-y-3 rounded-md border border-gray-200 bg-white px-4 py-3" data-testid="mail-templates">
            <h2 id="mail-templates" className="text-lg font-semibold text-gray-900">{t('mail_templates_title')}</h2>
            <p className="text-sm text-gray-700">{t('mail_templates_text')} {value && <code className="text-xs">{value.placeholders.join(' ')}</code>}</p>
            {error && <ErrorNotice error={error} />}
            {value?.templates.map((template) => (
                <fieldset key={template.kind} disabled={busy !== null} className="space-y-1 rounded border border-gray-200 p-2" data-testid={`mail-template-${template.kind}`}>
                    <legend className="px-1 text-sm font-semibold">{t(`doc_kind_${template.kind}`)}{template.isDefault ? ` · ${t('mail_template_default')}` : ''}</legend>
                    <label htmlFor={`template-${template.kind}-subject`} className="text-xs font-medium text-gray-700">{t('mail_subject')}</label>
                    <input id={`template-${template.kind}-subject`} value={template.subject} maxLength={300} onChange={(event) => change(template.kind, { subject: event.target.value })} className={inputClass} />
                    <label htmlFor={`template-${template.kind}-body`} className="text-xs font-medium text-gray-700">{t('mail_body')}</label>
                    <textarea id={`template-${template.kind}-body`} rows={7} value={template.body} maxLength={10000} onChange={(event) => change(template.kind, { body: event.target.value })} className={`${inputClass} font-mono text-xs`} />
                    <div className="flex flex-wrap items-center gap-2">
                        <button type="button" className={primaryButtonClass} onClick={() => void save({ kind: template.kind, subject: template.subject, body: template.body })}>{t('loading_slip_save')}</button>
                        {!template.isDefault && <button type="button" className={secondaryButtonClass} onClick={() => void save({ kind: template.kind, subject: '', body: '' })}>{t('mail_template_reset')}</button>}
                        {saved === template.kind && <span role="status" className="text-sm text-green-800">{t('loading_slip_saved')}</span>}
                    </div>
                </fieldset>
            ))}
        </section>
    );
}
