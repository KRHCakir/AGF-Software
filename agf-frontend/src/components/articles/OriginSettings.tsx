'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { countryName } from '../../lib/countries';
import { ApiError, toApiError } from '../../services/apiClient';
import { addCountryAlias, getOriginCleanup, removeCountryAlias, setArticleOriginCountry } from '../../services/countryService';
import type { Article } from '../../types/article';
import type { CountryReference, OriginCleanupItem } from '../../types/country';
import CountryPicker from '../shared/CountryPicker';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { readStoredActor } from '../supply/useActorReference';

const section = 'rounded-md border border-gray-200 bg-white p-3';

// Herkomst in Artikelen: "Herkomst opschonen" (artikelen zonder land: kies er een land bij; nooit automatisch) en de eigen
// snelkoppelingen voor de landkiezer (bijv. "Kaap" -> Zuid-Afrika). Een snelkoppeling is alleen invoerhulp.
export default function OriginSettings({ countries, cleanupToken, onCountriesChanged, onArticleChanged }: {
    countries: CountryReference;
    cleanupToken: number;
    onCountriesChanged: (countries: CountryReference) => void;
    onArticleChanged: (article: Article) => void;
}) {
    const { t } = useTranslation();
    const [cleanup, setCleanup] = useState<OriginCleanupItem[] | null>(null);
    const [chosen, setChosen] = useState<Record<number, string | null>>({});
    const [alias, setAlias] = useState({ text: '', code: null as string | null });
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getOriginCleanup(controller.signal)
            .then((items) => {
                setCleanup(items);
                // Precies één voorstel (bijv. oude tekst "Peru"): alvast ingevuld, opslaan blijft een bewuste klik.
                setChosen(Object.fromEntries(items.map((item) => [item.id, item.suggestions.length === 1 ? item.suggestions[0] : null])));
            })
            .catch((caught: unknown) => { if (toApiError(caught).kind !== 'aborted') setError(toApiError(caught)); });
        return () => controller.abort();
    }, [cleanupToken]);

    const run = async (action: () => Promise<void>) => {
        setBusy(true);
        setError(null);
        try { await action(); } catch (caught) { setError(toApiError(caught)); } finally { setBusy(false); }
    };

    const saveOrigin = (item: OriginCleanupItem) => run(async () => {
        const code = chosen[item.id];
        if (!code) return;
        onArticleChanged(await setArticleOriginCountry(item.id, code));
        setCleanup((current) => (current ?? []).filter((candidate) => candidate.id !== item.id));
    });

    const saveAlias = () => run(async () => {
        if (alias.text.trim() === '' || alias.code === null) return;
        const saved = await addCountryAlias(alias.text.trim(), alias.code, readStoredActor());
        onCountriesChanged({ ...countries, aliases: [...countries.aliases.filter((a) => a.id !== saved.id), saved].sort((a, b) => a.alias.localeCompare(b.alias)) });
        setAlias({ text: '', code: null });
    });

    return (
        <div className="grid gap-3 lg:grid-cols-2">
            {error && <div className="lg:col-span-2"><ErrorNotice error={error} /></div>}
            <section className={section} aria-labelledby="origin-cleanup" data-tour="origin-cleanup">
                <h2 id="origin-cleanup" className="text-sm font-semibold text-gray-900">{t('origin_cleanup_title')}</h2>
                <p className="text-xs text-gray-600">{t('origin_cleanup_hint')}</p>
                {cleanup === null && <p className="mt-2 text-sm text-gray-600">{t('articles_loading')}</p>}
                {cleanup !== null && cleanup.length === 0 && <p className="mt-2 text-sm text-green-800">{t('origin_cleanup_empty')}</p>}
                <ul className="mt-2 divide-y divide-gray-100 text-sm">
                    {(cleanup ?? []).map((item) => (
                        <li key={item.id} className="flex flex-col gap-1 py-2 sm:flex-row sm:items-end sm:gap-2" data-testid={`origin-cleanup-${item.id}`}>
                            <span className="min-w-0 flex-1 break-words">
                                <span className="font-medium text-gray-900">{item.description}</span>
                                <span className="block text-xs text-gray-600">{t('origin_cleanup_old_text', { origin: item.origin ?? '–' })}</span>
                            </span>
                            <span className="w-full sm:w-56">
                                <CountryPicker id={`origin-cleanup-${item.id}`} countries={countries} value={chosen[item.id] ?? null} disabled={busy}
                                    onChange={(code) => setChosen((current) => ({ ...current, [item.id]: code }))} />
                            </span>
                            <button type="button" className={secondaryButtonClass} disabled={busy || !chosen[item.id]} onClick={() => void saveOrigin(item)}>{t('origin_cleanup_save')}</button>
                        </li>
                    ))}
                </ul>
            </section>

            <section className={section} aria-labelledby="country-aliases" data-tour="country-aliases">
                <h2 id="country-aliases" className="text-sm font-semibold text-gray-900">{t('country_alias_title')}</h2>
                <p className="text-xs text-gray-600">{t('country_alias_hint')}</p>
                <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-end">
                    <label className="text-xs font-medium text-gray-700 sm:w-32">
                        {t('country_alias_alias')}
                        <input value={alias.text} maxLength={40} disabled={busy} onChange={(event) => setAlias((current) => ({ ...current, text: event.target.value }))} className={inputClass} />
                    </label>
                    <span className="flex-1">
                        <span className="text-xs font-medium text-gray-700">{t('country_alias_country')}</span>
                        <CountryPicker id="country-alias-country" countries={countries} value={alias.code} disabled={busy} onChange={(code) => setAlias((current) => ({ ...current, code }))} />
                    </span>
                    <button type="button" className={primaryButtonClass} disabled={busy || alias.text.trim() === '' || alias.code === null} onClick={() => void saveAlias()}>{t('country_alias_add')}</button>
                </div>
                {countries.aliases.length === 0 && <p className="mt-2 text-sm text-gray-600">{t('country_alias_none')}</p>}
                <ul className="mt-2 flex flex-wrap gap-2 text-sm">
                    {countries.aliases.map((entry) => (
                        <li key={entry.id} className="inline-flex items-center gap-1 rounded border border-gray-300 px-2 py-0.5">
                            <span className="font-mono">{entry.alias}</span> → {countryName(countries, entry.countryCode)}
                            <button type="button" disabled={busy} aria-label={t('country_alias_remove', { alias: entry.alias })} className="ml-1 text-gray-600 hover:text-red-800"
                                onClick={() => void run(async () => onCountriesChanged(await removeCountryAlias(entry.id)))}>×</button>
                        </li>
                    ))}
                </ul>
            </section>
        </div>
    );
}
