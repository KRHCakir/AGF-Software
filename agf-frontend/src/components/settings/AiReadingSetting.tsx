'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatMoney } from '../../lib/availability';
import { parseLocaleDecimal } from '../../lib/decimal';
import { ApiError, toApiError } from '../../services/apiClient';
import { type AiReadingSettings, getAiReadingSettings, getAiReadingStatus, updateAiReadingSettings } from '../../services/aiReadingService';
import type { AiReadingStatus } from '../../types/aiReading';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Beheer: AI-lezen van inkoopfacturen. Standaard uit. Aanzetten stuurt documenten naar een externe dienst (de aanbieder staat erbij); de
// beheerder bevestigt dat de klant daarvan op de hoogte is, en het systeem legt vast wie dat deed en wanneer. De maandlimiet in euro
// pauzeert het lezen; bereikt, dan staat dat hier in het rood. Handmatig invoeren blijft altijd werken.
export default function AiReadingSetting() {
    const { t, locale } = useTranslation();
    const [settings, setSettings] = useState<AiReadingSettings | null>(null);
    const [status, setStatus] = useState<AiReadingStatus | null>(null);
    const [informed, setInformed] = useState(false);
    const [limit, setLimit] = useState('');
    const [error, setError] = useState<ApiError | null>(null);
    const [limitError, setLimitError] = useState<string[] | undefined>(undefined);
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([getAiReadingSettings(controller.signal), getAiReadingStatus(controller.signal).catch(() => null)])
            .then(([loaded, current]) => { setSettings(loaded); setStatus(current); setLimit(String(loaded.monthlyLimitEur).replace('.', ',')); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const save = async (enabled: boolean, event?: FormEvent) => {
        event?.preventDefault();
        if (saving || settings === null) return;
        const parsed = parseLocaleDecimal(limit);
        if (!parsed.ok || parsed.value < 0) { setLimitError([t('ai_settings_limit_invalid')]); return; }
        setSaving(true);
        setError(null);
        setLimitError(undefined);
        try {
            const result = await updateAiReadingSettings({ enabled, customerInformed: informed, monthlyLimitEur: parsed.value });
            setSettings(result);
            setInformed(false);
            setStatus(await getAiReadingStatus().catch(() => status));
        } catch (caught) {
            const apiError = toApiError(caught);
            setError(apiError);
            setLimitError(apiError.fieldErrors.monthlyLimitEur);
        } finally {
            setSaving(false);
        }
    };

    return (
        <form id="ai-lezen" noValidate onSubmit={(event) => void save(settings?.enabled ?? false, event)} className="space-y-2 rounded-md border border-gray-300 bg-white p-3"
            data-testid="ai-reading-setting" data-tour="ai-reading-setting">
            <h2 className="text-lg font-semibold text-gray-900">{t('ai_settings_title')}</h2>
            <p className="text-sm text-gray-700">{t(status?.fallbackEnabled && status.fallbackModel ? 'ai_settings_hint' : 'ai_settings_hint_single', { provider: status?.provider === 'Demo' ? t('ai_settings_provider_demo') : t('ai_settings_provider'), model: status?.primaryModel.name ?? '–', fallback: status?.fallbackModel?.name ?? '–' })}</p>
            {error && <ErrorNotice error={error} />}
            {settings && (
                <>
                    <p className={`text-sm font-medium ${settings.enabled ? 'text-green-900' : 'text-gray-900'}`} data-testid="ai-reading-state" data-tour={settings.enabled ? 'ai-reading-on' : 'ai-reading-off'}>
                        {settings.enabled
                            ? t('ai_settings_on', { name: settings.enabledBy ?? '–', date: settings.enabledAt ? formatDateTime(settings.enabledAt, locale) : '–' })
                            : t('ai_settings_off')}
                    </p>
                    {status && !status.configured && <p className="text-sm text-amber-900">{t('ai_not_configured', { reason: status.notConfiguredReason ?? '' })}</p>}
                    {status?.limitReached && (
                        <p role="alert" className="rounded border-2 border-red-400 bg-red-50 px-2 py-1 text-sm font-medium text-red-950" data-testid="ai-limit-reached">
                            {t('ai_settings_limit_reached', { spent: formatMoney(status.monthSpentEur, 'EUR', locale), limit: formatMoney(status.monthlyLimitEur, 'EUR', locale) })}
                        </p>
                    )}
                    {status && !status.limitReached && <p className="text-xs text-gray-600">{t('ai_settings_spent', { spent: formatMoney(status.monthSpentEur, 'EUR', locale), limit: formatMoney(status.monthlyLimitEur, 'EUR', locale) })}</p>}
                    <div className="max-w-xs">
                        <label htmlFor="aiMonthlyLimit-input" className="text-xs font-medium text-gray-700">{t('ai_settings_limit')}</label>
                        <input {...fieldProps('aiMonthlyLimit', limitError)} inputMode="decimal" value={limit} onChange={(event) => setLimit(event.target.value)} className={inputClass} />
                        <FieldError id="aiMonthlyLimit" messages={limitError} />
                    </div>
                    {!settings.enabled && (
                        <label className="flex items-start gap-2 text-sm text-gray-900">
                            <input type="checkbox" className="mt-1" checked={informed} onChange={(event) => setInformed(event.target.checked)} data-tour="ai-reading-informed" />
                            <span>{t('ai_settings_informed')}</span>
                        </label>
                    )}
                    <div className="flex flex-wrap gap-2">
                        {settings.enabled
                            ? <button type="button" className={secondaryButtonClass} disabled={saving} onClick={() => void save(false)}>{t('ai_settings_turn_off')}</button>
                            : <button type="button" className={primaryButtonClass} disabled={saving || !informed} onClick={() => void save(true)} data-tour="ai-reading-enable">{t('ai_settings_turn_on')}</button>}
                        <button type="submit" className={secondaryButtonClass} disabled={saving}>{t('ai_settings_save_limit')}</button>
                    </div>
                </>
            )}
        </form>
    );
}
