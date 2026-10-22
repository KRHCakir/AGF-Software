'use client';

import { KeyboardEvent, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatGoodsCode } from '../../lib/articleRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { searchGoodsCodes } from '../../services/articleService';
import type { GoodsCodeSearch, GoodsCodeSystem } from '../../types/article';
import { GOODS_CODE_SYSTEMS, isGoodsCodeSystem } from '../../types/article';
import { inputClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

export const OFFICIAL_TARIC_URL = 'https://ec.europa.eu/taxation_customs/dds2/taric/taric_consultation.jsp?Lang=nl';

// Zoeken in de lokale referentielijst van goederencodes, altijd binnen het gekozen codestelsel: HS geeft 6-cijferige posten
// (dubbele GN-treffers samengevoegd), GN/CN de 8-cijferige codes, TARIC alleen daadwerkelijk opgenomen 10-cijferige
// referenties (nooit een GN-code met "00" erachter). Bron, versie en onvolledigheid staan altijd bij de treffers; de
// gebruiker neemt zelf een code over en het overnemen verandert het gekozen stelsel nooit. Bij een stelselwissel verdwijnen
// oude treffers en wordt een laat antwoord van een eerdere zoekopdracht genegeerd. De officiële raadpleging blijft één klik weg.
export default function GoodsCodeLookup({ system, onSystemChange, onPick, onManual, disabled = false }: {
    system: GoodsCodeSystem | '';
    onSystemChange: (system: GoodsCodeSystem | '') => void;
    onPick: (code: string, system: GoodsCodeSystem) => void;
    onManual: () => void;
    disabled?: boolean;
}) {
    const { t, locale } = useTranslation();
    const [query, setQuery] = useState('');
    const [result, setResult] = useState<GoodsCodeSearch | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [loading, setLoading] = useState(false);
    // Volgnummer van de laatste zoekopdracht: alleen het antwoord op de laatste opdracht telt.
    const sequence = useRef(0);
    const controller = useRef<AbortController | null>(null);

    useEffect(() => {
        sequence.current += 1;
        controller.current?.abort();
        controller.current = null;
        setResult(null);
        setError(null);
        setLoading(false);
    }, [system]);
    useEffect(() => () => controller.current?.abort(), []);

    const search = async () => {
        if (query.trim() === '' || system === '' || disabled) return;
        controller.current?.abort();
        const own = new AbortController();
        controller.current = own;
        const ticket = ++sequence.current;
        const searched = system;
        setLoading(true);
        setError(null);
        try {
            const found = await searchGoodsCodes(query, searched, own.signal);
            if (ticket !== sequence.current) return; // verouderd: stelsel gewisseld of nieuwe zoekopdracht gestart
            // Nooit treffers van een ander stelsel tonen, ook niet als de server er onverhoopt een terugstuurt.
            setResult({ ...found, matches: found.matches.filter((match) => match.system === searched) });
        } catch (caught) {
            if (ticket !== sequence.current) return;
            const apiError = toApiError(caught);
            if (apiError.kind !== 'aborted') setError(apiError);
        } finally {
            if (ticket === sequence.current) setLoading(false);
        }
    };
    const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'Enter') { event.preventDefault(); void search(); }
    };
    const description = (match: { descriptionNl: string | null; descriptionEn: string | null }) =>
        (locale === 'nl' ? match.descriptionNl : match.descriptionEn) ?? match.descriptionEn ?? match.descriptionNl ?? '';
    const lookupUrl = result?.source.lookupUrl ?? OFFICIAL_TARIC_URL;

    return (
        <div className="rounded border border-gray-200 bg-gray-50 p-2 text-sm" data-testid="goods-code-lookup">
            <div className="flex flex-wrap gap-2">
                <label htmlFor="goodsCodeLookupSystem-input" className="sr-only">{t('articles_field_goods_system')}</label>
                <select id="goodsCodeLookupSystem-input" value={system} disabled={disabled} className={`${inputClass} mt-0 max-w-xs`}
                    onChange={(event) => onSystemChange(isGoodsCodeSystem(event.target.value) ? event.target.value : '')}>
                    <option value="">{t('articles_goods_choose_system_option')}</option>
                    {GOODS_CODE_SYSTEMS.map((option) => <option key={option} value={option}>{t(`articles_goods_system_${option}`)}</option>)}
                </select>
                <label htmlFor="goodsCodeQuery-input" className="sr-only">{t('articles_goods_search')}</label>
                <input id="goodsCodeQuery-input" value={query} disabled={disabled} placeholder={t('articles_goods_query_placeholder')} onKeyDown={onKeyDown}
                    onChange={(event) => setQuery(event.target.value)} className={`${inputClass} mt-0 min-w-48 flex-1`} />
                <button type="button" className={secondaryButtonClass} disabled={disabled || system === ''} onClick={() => void search()}>{t('articles_goods_search')}</button>
            </div>
            {system === '' && <p className="mt-1 text-xs text-gray-700">{t('articles_goods_choose_system')}</p>}
            {loading && <p className="mt-1 text-xs text-gray-600" role="status">{t('availability_loading')}</p>}
            {error && <p role="alert" className="mt-1 text-xs text-red-800">{error.message}</p>}
            {result && system !== '' && (
                <div className="mt-2 space-y-1" data-testid={`goods-code-results-${system}`}>
                    {result.matches.length === 0 && (
                        <div className="rounded border border-amber-300 bg-amber-50 px-2 py-1 text-xs text-amber-900">
                            <p>{t(system === 'TARIC' ? 'articles_goods_none_taric' : 'articles_goods_none', { system: t(`articles_goods_system_${system}`) })}</p>
                            <p className="mt-1 flex flex-wrap gap-2">
                                <a href={lookupUrl} target="_blank" rel="noreferrer" className="font-medium text-blue-800 underline underline-offset-2">{t('articles_goods_official')}</a>
                                <button type="button" className="font-medium text-blue-800 underline underline-offset-2" onClick={onManual} disabled={disabled}>{t('articles_goods_manual')}</button>
                            </p>
                        </div>
                    )}
                    {result.matches.length > 0 && (
                        <ul className="divide-y divide-gray-200 rounded border border-gray-200 bg-white">
                            {result.matches.map((match) => (
                                <li key={`${match.system}:${match.code}`} className="flex items-center justify-between gap-2 px-2 py-1">
                                    <span>
                                        <span className="font-mono font-semibold">{formatGoodsCode(match.code)}</span>
                                        <span className="ml-1 rounded bg-gray-100 px-1 text-xs text-gray-700">{match.system}</span>
                                        <span className="ml-2 text-gray-800">{description(match)}</span>
                                        {match.mergedCodes !== null && match.mergedCodes.length > 0 && (
                                            <span className="block text-xs text-gray-600">{t('articles_goods_merged', { count: String(match.mergedCodes.length), codes: match.mergedCodes.map(formatGoodsCode).join(', ') })}</span>
                                        )}
                                    </span>
                                    <button type="button" className={`${secondaryButtonClass} shrink-0 px-2 py-0.5 text-xs`} disabled={disabled || !isGoodsCodeSystem(match.system) || match.system !== system}
                                        onClick={() => { if (isGoodsCodeSystem(match.system) && match.system === system) onPick(match.code, match.system); }}>{t('articles_goods_pick')}</button>
                                </li>
                            ))}
                        </ul>
                    )}
                    <p className="text-xs text-gray-600">
                        {t('articles_goods_source', { name: result.source.name })}{' '}
                        {result.source.version && t('articles_goods_version', { version: result.source.version, validFrom: result.source.validFrom ?? '–', retrievedAt: result.source.retrievedAt ?? '–' })}{' '}
                        {t('articles_goods_incomplete', { count: String(result.totalEntries), system })}
                    </p>
                    {result.source.note && <p className="text-xs text-gray-600">{result.source.note}</p>}
                </div>
            )}
            <p className="mt-1 text-xs">
                <a href={lookupUrl} target="_blank" rel="noreferrer" className="font-medium text-blue-800 underline underline-offset-2">{t('articles_goods_official')}</a>
            </p>
        </div>
    );
}
