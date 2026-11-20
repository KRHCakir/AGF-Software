'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { todayInAmsterdam } from '../../lib/calendarDate';
import { parseLocaleDecimal } from '../../lib/decimal';
import { ApiError, toApiError } from '../../services/apiClient';
import { getCertificateSettings, saveCertificateSettings } from '../../services/certificateService';
import CertificatesEditor from '../certificates/CertificatesEditor';
import { certificateMessages, useCertificates } from '../certificates/useCertificates';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Instellingen › Certificaten (certificaten deel 1): hoeveel dagen vooraf de app waarschuwt (standaard 30), de eigen soorten (één per regel)
// en de certificaten van het eigen bedrijf, met hetzelfde blok als bij een relatie (eigen knop Opslaan).
export default function CertificatesSetting() {
    const { t } = useTranslation();
    const [days, setDays] = useState('');
    const [types, setTypes] = useState('');
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [notice, setNotice] = useState<string | null>(null);
    const own = useCertificates({ kind: 'own' });
    const [ownSaving, setOwnSaving] = useState(false);
    const [ownNotice, setOwnNotice] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getCertificateSettings(controller.signal).then((settings) => { setDays(String(settings.warningDays)); setTypes(settings.customTypes.join('\n')); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const saveSettings = async () => {
        setSaving(true);
        setError(null);
        setNotice(null);
        try {
            // Een geheel aantal dagen via de gedeelde invoerparser; iets anders laat de server weigeren (met de melding erbij).
            const parsed = parseLocaleDecimal(days, undefined, { maxDecimals: 0 });
            const stored = await saveCertificateSettings({ warningDays: parsed.ok ? parsed.value : 0, customTypes: types.split('\n').map((line) => line.trim()).filter(Boolean) });
            setDays(String(stored.warningDays));
            setTypes(stored.customTypes.join('\n'));
            setNotice(t('cert_settings_saved'));
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setSaving(false);
        }
    };

    const saveOwn = async () => {
        setOwnSaving(true);
        setOwnNotice(false);
        try {
            if (await own.save()) setOwnNotice(true);
        } finally {
            setOwnSaving(false);
        }
    };

    return (
        <section id="certificaten" className="space-y-3 rounded-md border border-gray-300 bg-white p-3" data-testid="certificates-setting" aria-labelledby="certificates-setting-title">
            <h2 id="certificates-setting-title" className="text-lg font-semibold text-gray-900">{t('cert_section_title')}</h2>
            {error && <ErrorNotice error={error} />}
            <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                <div>
                    <label htmlFor="certificate-warning-days" className="text-xs font-medium text-gray-700">{t('cert_settings_days')}</label>
                    <input id="certificate-warning-days" inputMode="numeric" value={days} disabled={saving} className={`${inputClass} max-w-[8rem]`}
                        onChange={(event) => setDays(event.target.value)} aria-invalid={!!error?.fieldErrors.warningDays} />
                    <span className="block text-xs text-gray-500">{t('cert_settings_days_hint')}</span>
                </div>
                <div className="md:col-span-2">
                    <label htmlFor="certificate-custom-types" className="text-xs font-medium text-gray-700">{t('cert_settings_types')}</label>
                    <textarea id="certificate-custom-types" rows={3} value={types} disabled={saving} className={inputClass} onChange={(event) => setTypes(event.target.value)} />
                    <span className="block text-xs text-gray-500">{t('cert_settings_types_hint')}</span>
                </div>
            </div>
            <div className="flex flex-wrap items-center gap-2">
                <button type="button" className={secondaryButtonClass} disabled={saving || days === ''} onClick={() => void saveSettings()} data-testid="certificate-settings-save">{t('cert_settings_save')}</button>
                {notice && <span role="status" className="text-sm text-green-900">{notice}</span>}
            </div>

            <div className="space-y-2 border-t border-gray-200 pt-3" data-tour="own-certificates">
                <h3 className="text-sm font-semibold text-gray-900">{t('cert_own_title')}</h3>
                <p className="text-xs text-gray-600">{t('cert_own_hint')}</p>
                {own.error && <ErrorNotice error={own.error} />}
                {own.conflict && <p role="alert" className="text-sm text-amber-950">{t('cert_conflict')}</p>}
                {(own.conflict || own.failed) && !own.loading && <button type="button" className={secondaryButtonClass} onClick={own.reload}>{t('cert_reload')}</button>}
                {own.reference !== null && (
                    <CertificatesEditor idPrefix="own-certificates" rows={own.rows} onChange={(rows) => { own.setRows(rows); setOwnNotice(false); }} reference={own.reference} products={own.products}
                        errors={certificateMessages(own.errors, own.problems, t)} today={todayInAmsterdam()} warningDays={own.warningDays} disabled={ownSaving || own.loading || own.failed}
                        onOpenFile={(id, file) => void own.openFile(id, file)} onAddFile={(row, file) => void own.addFile(row, file)} uploading={own.uploading} dataTour="own-certificate" />
                )}
                <div className="flex flex-wrap items-center gap-2">
                    <button type="button" className={primaryButtonClass} disabled={ownSaving || own.loading || own.failed || !own.changed} onClick={() => void saveOwn()} data-testid="own-certificates-save">{t('cert_own_save')}</button>
                    {ownNotice && <span role="status" className="text-sm text-green-900">{t('cert_own_saved')}</span>}
                </div>
            </div>
        </section>
    );
}
