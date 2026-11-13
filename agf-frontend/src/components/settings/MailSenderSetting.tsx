'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getMailSettings, saveMailSettings } from '../../services/documentMailService';
import type { MailSettings } from '../../types/documentMail';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass } from '../supply/SuccessPanel';

// De afzender (documenten deel 3, Beheer): SMTP-server, poort, versleuteling, gebruiker, wachtwoord, van-adres, naam en handtekening. Het
// wachtwoord gaat er alleen in: het veld blijft leeg en zegt of er een is ingesteld; leeg laten = ongewijzigd. Microsoft 365 volgt later.
export default function MailSenderSetting() {
    const { t } = useTranslation();
    const [value, setValue] = useState<MailSettings | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getMailSettings(controller.signal).then((found) => setValue({ ...found, password: '', clearPassword: false }))
            .catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const change = (patch: Partial<MailSettings>) => { if (value) { setValue({ ...value, ...patch }); setSaved(false); } };
    const save = async (event: FormEvent) => {
        event.preventDefault();
        if (value === null || saving) return;
        setSaving(true);
        setError(null);
        try {
            const result = await saveMailSettings({ ...value, password: value.password?.trim() ? value.password : null });
            setValue({ ...result, password: '', clearPassword: false });
            setSaved(true);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setSaving(false);
        }
    };
    const field = (key: keyof MailSettings, label: string, extra: { type?: string; placeholder?: string; autoComplete?: string } = {}) => (
        <div>
            <label htmlFor={`mail-${key}`} className="text-xs font-medium text-gray-700">{label}</label>
            <input id={`mail-${key}`} type={extra.type ?? 'text'} autoComplete={extra.autoComplete ?? 'off'} placeholder={extra.placeholder} value={String(value?.[key] ?? '')}
                onChange={(event) => change({ [key]: key === 'port' ? Number(event.target.value) || 0 : event.target.value } as Partial<MailSettings>)} className={inputClass} />
        </div>
    );

    return (
        <section aria-labelledby="mail-sender" className="space-y-2 rounded-md border border-gray-200 bg-white px-4 py-3" data-testid="mail-sender" data-tour="mail-sender">
            <h2 id="mail-sender" className="text-lg font-semibold text-gray-900">{t('mail_sender_title')}</h2>
            <p className="text-sm text-gray-700">{t('mail_sender_text')}</p>
            {value?.sender === 'Fake' && <p role="status" className="rounded border border-amber-300 bg-amber-50 px-2 py-1 text-sm text-amber-950" data-testid="mail-sender-fake">{t('mail_sender_fake')}</p>}
            {value && <p className="text-sm" data-testid="mail-sender-state">{t(value.configured ? 'mail_sender_configured' : 'mail_sender_not_configured')}</p>}
            {error && <ErrorNotice error={error} />}
            {value && (
                <form onSubmit={(event) => void save(event)} className="space-y-2">
                    <fieldset disabled={saving} className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                        {field('host', t('mail_host'), { placeholder: 'smtp.voorbeeld.nl' })}
                        {field('port', t('mail_port'))}
                        <div>
                            <label htmlFor="mail-security" className="text-xs font-medium text-gray-700">{t('mail_security')}</label>
                            <select id="mail-security" value={value.security} onChange={(event) => change({ security: event.target.value as MailSettings['security'] })} className={inputClass}>
                                <option value="StartTls">{t('mail_security_StartTls')}</option>
                                <option value="SslOnConnect">{t('mail_security_SslOnConnect')}</option>
                                <option value="None">{t('mail_security_None')}</option>
                            </select>
                        </div>
                        {field('userName', t('mail_user'), { autoComplete: 'username' })}
                        <div>
                            <label htmlFor="mail-password" className="text-xs font-medium text-gray-700">{t('mail_password')}</label>
                            <input id="mail-password" type="password" autoComplete="new-password" value={value.password ?? ''} placeholder={value.hasPassword ? t('mail_password_set') : ''}
                                onChange={(event) => change({ password: event.target.value, clearPassword: false })} className={inputClass} />
                            {value.hasPassword && (
                                <label className="mt-1 flex items-center gap-1 text-xs"><input type="checkbox" checked={value.clearPassword} onChange={(event) => change({ clearPassword: event.target.checked, password: '' })} /> {t('mail_password_clear')}</label>
                            )}
                        </div>
                        {field('fromAddress', t('mail_from_address'), { type: 'email', placeholder: 'verkoop@voorbeeld.nl' })}
                        {field('fromName', t('mail_from_name'))}
                        <div className="sm:col-span-3">
                            <label htmlFor="mail-signature" className="text-xs font-medium text-gray-700">{t('mail_signature')}</label>
                            <textarea id="mail-signature" rows={3} maxLength={2000} value={value.signature ?? ''} onChange={(event) => change({ signature: event.target.value })} className={inputClass} />
                        </div>
                    </fieldset>
                    <div className="flex items-center gap-2">
                        <button type="submit" className={primaryButtonClass} disabled={saving}>{t('loading_slip_save')}</button>
                        {saved && <span role="status" className="text-sm text-green-800">{t('loading_slip_saved')}</span>}
                    </div>
                </form>
            )}
        </section>
    );
}
