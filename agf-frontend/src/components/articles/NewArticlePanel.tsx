'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getArticleReference } from '../../services/articleService';
import { getCountries } from '../../services/countryService';
import type { Article, ArticleReference } from '../../types/article';
import type { CountryReference } from '../../types/country';
import SidePanel from '../shared/SidePanel';
import ErrorNotice from '../supply/ErrorNotice';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import ArticleForm from './ArticleForm';

// "Nieuw artikel" vanuit een order (feedback 22 november): het artikelformulier in een zijpaneel over de order, compact (product,
// herkomst, klasse, btw-tarief, eenheid, kg per colli; "Meer velden" voor de rest). Opslaan maakt het artikel aan (zelfde route als
// Artikelen) en geeft het terug; de order zet het op de regel waar de gebruiker was. Escape of Annuleren sluit; is er al iets ingevuld,
// dan eerst de vraag of dat weg mag. De order eronder blijft open en ongewijzigd.
export default function NewArticlePanel({ onSaved, onClose }: { onSaved: (article: Article) => void; onClose: () => void }) {
    const { t } = useTranslation();
    const [reference, setReference] = useState<ArticleReference | null>(null);
    const [countries, setCountries] = useState<CountryReference | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [asking, setAsking] = useState(false);
    const dirty = useRef(false);
    const stayButton = useRef<HTMLButtonElement | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([getArticleReference(controller.signal), getCountries(controller.signal)])
            .then(([loadedReference, loadedCountries]) => { setReference(loadedReference); setCountries(loadedCountries); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);
    useEffect(() => { if (asking) stayButton.current?.focus(); }, [asking]);

    const requestClose = () => {
        if (asking) { setAsking(false); return; }
        if (dirty.current) setAsking(true);
        else onClose();
    };

    return (
        <SidePanel title={t('new_article_panel_title')} onRequestClose={requestClose} testId="new-article-panel" dataTour="new-article-panel">
            <div onInput={() => { dirty.current = true; }} onChange={() => { dirty.current = true; }}>
                {error && <ErrorNotice error={error} />}
                {error === null && (reference === null || countries === null) && <p className="text-sm text-gray-600">{t('articles_loading')}</p>}
                {reference !== null && countries !== null && (
                    <ArticleForm compact article={null} reference={reference} countries={countries} onClose={requestClose}
                        onSaved={(article) => { dirty.current = false; onSaved(article); }} />
                )}
            </div>
            {asking && (
                <div role="alertdialog" aria-modal="true" aria-labelledby="new-article-discard-title" className="mt-3 rounded-md border border-amber-300 bg-amber-50 p-3" data-testid="new-article-discard">
                    <p id="new-article-discard-title" className="text-sm font-semibold text-gray-900">{t('new_article_panel_discard_question')}</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                        <button ref={stayButton} type="button" className={primaryButtonClass} onClick={() => setAsking(false)}>{t('new_article_panel_stay')}</button>
                        <button type="button" className={secondaryButtonClass} onClick={onClose} data-tour="new-article-discard">{t('new_article_panel_discard')}</button>
                    </div>
                </div>
            )}
        </SidePanel>
    );
}
