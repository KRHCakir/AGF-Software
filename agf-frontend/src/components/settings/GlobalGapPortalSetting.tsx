'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { MAX_PORTAL_URL_LENGTH, isHttpsUrl } from '../../lib/relationRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { getRelationSettings, updateRelationSettings } from '../../services/relationService';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass } from '../supply/SuccessPanel';

// Bedrijfsinstelling: adres van de GLOBALG.A.P. Supply Chain Portal. De knop "Controleer bij GLOBALG.A.P." op de leverancierskaart
// opent dit adres; de controle zelf blijft handwerk (datum en naam leggen vast wie keek). Alleen absolute https-adressen.
export default function GlobalGapPortalSetting() {
    const { t } = useTranslation();
    const [value, setValue] = useState<string | null>(null);
    const [saved, setSaved] = useState<string | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [error, setError] = useState<string[] | undefined>(undefined);
    const [serverError, setServerError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [notice, setNotice] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getRelationSettings(controller.signal)
            .then((settings) => { setValue(settings.globalGapPortalUrl); setSaved(settings.globalGapPortalUrl); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, []);

    const submit = async (event: FormEvent) => {
        event.preventDefault();
        if (saving || value === null) return;
        if (!isHttpsUrl(value)) { setError([t('settings_portal_url_invalid')]); return; }
        setSaving(true);
        setServerError(null);
        try {
            const result = await updateRelationSettings({ globalGapPortalUrl: value.trim() });
            setValue(result.globalGapPortalUrl);
            setSaved(result.globalGapPortalUrl);
            setNotice(true);
        } catch (caught) {
            const apiError = toApiError(caught);
            setServerError(apiError);
            setError(apiError.fieldErrors.globalGapPortalUrl);
        } finally {
            setSaving(false);
        }
    };

    return (
        <form noValidate onSubmit={submit} className="space-y-2 rounded-md border border-gray-300 bg-white p-3" data-testid="globalgap-portal-setting">
            <h2 className="text-lg font-semibold text-gray-900">{t('settings_portal_title')}</h2>
            <p className="text-sm text-gray-600">{t('settings_portal_hint')}</p>
            {loadError && <ErrorNotice error={loadError} />}
            {serverError && <ErrorNotice error={serverError} />}
            {notice && <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">{t('settings_portal_saved')}</p>}
            <div>
                <label htmlFor="globalGapPortalUrl-input" className="text-xs font-medium text-gray-700">{t('settings_portal_url')}</label>
                <input {...fieldProps('globalGapPortalUrl', error)} type="url" inputMode="url" value={value ?? ''} maxLength={MAX_PORTAL_URL_LENGTH} disabled={value === null || saving}
                    onChange={(event) => { setValue(event.target.value); setError(undefined); setNotice(false); }} className={`${inputClass} font-mono text-xs`} />
                <FieldError id="globalGapPortalUrl" messages={error} />
            </div>
            <div className="flex flex-wrap items-center gap-2">
                <button type="submit" className={primaryButtonClass} disabled={value === null || saving || value === saved}>{saving ? t('supply_saving') : t('settings_portal_save')}</button>
                {saved && <a href={saved} target="_blank" rel="noopener noreferrer" className="text-xs font-medium text-blue-800 underline underline-offset-2">{t('settings_portal_open')}</a>}
            </div>
        </form>
    );
}
