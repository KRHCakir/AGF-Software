'use client';

import { KeyboardEvent, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatGoodsCode } from '../../lib/articleRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { searchGoodsCodes } from '../../services/articleService';
import type { GoodsCodeSearch, GoodsCodeSystem } from '../../types/article';
import { isGoodsCodeSystem } from '../../types/article';
import { inputClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Zoeken in de lokale referentielijst van goederencodes. Bron, versie en geldigheid staan altijd bij de treffers; de lijst
// is nooit volledig en de gebruiker neemt zelf een code over (geen automatische indeling). De officiële raadpleging blijft
// één klik weg.
export default function GoodsCodeLookup({ onPick, disabled = false }: { onPick: (code: string, system: GoodsCodeSystem) => void; disabled?: boolean }) {
    const { t, locale } = useTranslation();
    const [query, setQuery] = useState('');
    const [result, setResult] = useState<GoodsCodeSearch | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [loading, setLoading] = useState(false);

    const search = async () => {
        if (query.trim() === '' || loading) return;
        setLoading(true);
        setError(null);
        try {
            setResult(await searchGoodsCodes(query));
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setLoading(false);
        }
    };
    const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'Enter') { event.preventDefault(); void search(); }
    };

    return (
        <div className="rounded border border-gray-200 bg-gray-50 p-2 text-sm" data-testid="goods-code-lookup">
            <div className="flex gap-2">
                <label htmlFor="goodsCodeQuery-input" className="sr-only">{t('articles_goods_search')}</label>
                <input id="goodsCodeQuery-input" value={query} disabled={disabled} placeholder={t('articles_goods_query_placeholder')} onKeyDown={onKeyDown}
                    onChange={(event) => setQuery(event.target.value)} className={`${inputClass} mt-0`} />
                <button type="button" className={secondaryButtonClass} disabled={disabled || loading} onClick={() => void search()}>{t('articles_goods_search')}</button>
            </div>
            {error && <p role="alert" className="mt-1 text-xs text-red-800">{error.message}</p>}
            {result && (
                <div className="mt-2 space-y-1">
                    {result.matches.length === 0 && <p className="text-xs text-gray-700">{t('articles_goods_none')}</p>}
                    {result.matches.length > 0 && (
                        <ul className="divide-y divide-gray-200 rounded border border-gray-200 bg-white">
                            {result.matches.map((match) => (
                                <li key={match.code} className="flex items-center justify-between gap-2 px-2 py-1">
                                    <span>
                                        <span className="font-mono font-semibold">{formatGoodsCode(match.code)}</span>
                                        <span className="ml-1 rounded bg-gray-100 px-1 text-xs text-gray-700">{match.system}</span>
                                        <span className="ml-2 text-gray-800">{(locale === 'nl' ? match.descriptionNl : match.descriptionEn) ?? match.descriptionEn ?? match.descriptionNl ?? ''}</span>
                                    </span>
                                    <button type="button" className={`${secondaryButtonClass} shrink-0 px-2 py-0.5 text-xs`} disabled={disabled || !isGoodsCodeSystem(match.system)}
                                        onClick={() => { if (isGoodsCodeSystem(match.system)) onPick(match.code, match.system); }}>{t('articles_goods_pick')}</button>
                                </li>
                            ))}
                        </ul>
                    )}
                    <p className="text-xs text-gray-600">
                        {t('articles_goods_source', { name: result.source.name })}{' '}
                        {result.source.version && t('articles_goods_version', { version: result.source.version, validFrom: result.source.validFrom ?? '–', retrievedAt: result.source.retrievedAt ?? '–' })}{' '}
                        {t('articles_goods_incomplete', { count: String(result.totalEntries) })}
                    </p>
                    {result.source.note && <p className="text-xs text-gray-600">{result.source.note}</p>}
                </div>
            )}
            <p className="mt-1 text-xs">
                <a href="https://ec.europa.eu/taxation_customs/dds2/taric/taric_consultation.jsp?Lang=nl" target="_blank" rel="noreferrer" className="font-medium text-blue-800 underline underline-offset-2">{t('articles_goods_official')}</a>
            </p>
        </div>
    );
}
