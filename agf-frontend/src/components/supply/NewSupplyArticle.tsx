'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { similarVariants } from '../../lib/articleFromEntry';
import { countryKey } from '../../lib/countries';
import { parsePositiveDecimal } from '../../lib/decimal';
import { EnteredAttributes, RECEIPT_ATTRIBUTES, ReceiptAttribute, emptyEntered } from '../../lib/receiptBlindEntry';
import { newRequestId } from '../../lib/requestId';
import { toApiError } from '../../services/apiClient';
import { createArticleFromEntry, existingArticle } from '../../services/articleService';
import { getCountries } from '../../services/countryService';
import type { Article } from '../../types/article';
import type { CountryReference } from '../../types/country';
import type { ProductOption, VariantOption } from '../../types/referenceData';
import CountryPicker from '../shared/CountryPicker';
import NewArticlePrompt from '../shared/NewArticlePrompt';
import { inputClass } from './FieldError';
import { secondaryButtonClass } from './SuccessPanel';
import { useActorReference } from './useActorReference';

const LABEL_KEYS: Record<ReceiptAttribute, string> = {
    size: 'receipt_attr_size', packaging: 'receipt_attr_packaging', tradeClass: 'receipt_attr_trade_class', brand: 'receipt_attr_brand',
};

interface ArticleKind { key: string; label: string; template: VariantOption; variants: VariantOption[] }

// Artikelen (productsoort + ras) waarvan al een variant bestaat; een nieuwe productsoort of een nieuw ras blijft in Artikelen.
export function articleKinds(products: readonly ProductOption[]): ArticleKind[] {
    const kinds = new Map<string, ArticleKind>();
    for (const product of products) {
        for (const variant of product.variants) {
            const key = `${product.id}|${countryKey(variant.name)}`;
            const kind = kinds.get(key);
            if (kind) kind.variants.push(variant);
            else kinds.set(key, { key, label: `${product.name} ${variant.name}`.trim(), template: variant, variants: [variant] });
        }
    }
    for (const kind of kinds.values()) kind.template = kind.variants.find((variant) => !variant.isArchived) ?? kind.template;
    return [...kinds.values()].sort((a, b) => a.label.localeCompare(b.label, 'nl'));
}

// Aanvoer plannen: een artikel dat nog niet bestaat meteen aanmaken met wat de gebruiker typt. De gebruiker kiest het artikel
// (productsoort en ras) en vult herkomst (verplicht) en zo nodig maat, verpakking, klasse, merk en kg per colli in; daarna dezelfde
// melding als bij ontvangst ("Nieuw artikel: …", "Bedoel je dit?", "Artikel aanmaken en doorgaan"). Het artikel komt op de regel.
// Tijdens het aanmaken liggen alle velden vast, zodat het artikel overeenkomt met wat er staat.
export default function NewSupplyArticle({ index, products, currentVariantId, onUse, onClose }: {
    index: number;
    products: readonly ProductOption[];
    currentVariantId: string;
    // Het gekozen of aangemaakte artikel voor deze regel (een nieuw artikel komt ook in de keuzelijst van het formulier).
    onUse: (variantId: number, created: Article | null) => void;
    onClose: () => void;
}) {
    const { t } = useTranslation();
    const [actor] = useActorReference();
    const kinds = useMemo(() => articleKinds(products), [products]);
    const [kindKey, setKindKey] = useState(() => kinds.find((kind) => kind.variants.some((variant) => String(variant.id) === currentVariantId))?.key ?? '');
    const [countries, setCountries] = useState<CountryReference | null>(null);
    const [entered, setEntered] = useState<EnteredAttributes>(emptyEntered);
    const [kg, setKg] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const request = useRef<{ key: string; requestId: string } | null>(null);
    const prefix = `lines[${index}].newArticle`;

    useEffect(() => {
        const controller = new AbortController();
        getCountries(controller.signal).then(setCountries).catch(() => setCountries(null));
        return () => controller.abort();
    }, []);

    const kind = kinds.find((candidate) => candidate.key === kindKey) ?? null;
    const names = useMemo(() => new Map((countries?.countries ?? []).map((country) => [country.code, country.nameNl])), [countries]);
    const perPackage = kg.trim() === '' ? null : parsePositiveDecimal(kg);

    const edit = (patch: Partial<EnteredAttributes>) => {
        setEntered((current) => ({ ...current, ...patch }));
        setError(null);
    };

    const create = async () => {
        if (kind === null || entered.origin === null || (perPackage !== null && !perPackage.ok)) return;
        const snapshot = JSON.stringify([kind.key, entered, kg]);
        if (request.current === null || request.current.key !== snapshot) request.current = { key: snapshot, requestId: newRequestId() };
        const text = (value: string) => (value.trim() === '' ? null : value.trim());
        setBusy(true);
        setError(null);
        try {
            const article = await createArticleFromEntry({
                requestId: request.current.requestId, source: 'Supply', templateVariantId: kind.template.id, originCountryCode: entered.origin,
                sizeCategory: text(entered.size), packaging: text(entered.packaging), tradeClass: text(entered.tradeClass), brand: text(entered.brand),
                netKgPerPackage: perPackage?.ok ? perPackage.value : null, actorReference: actor.trim() === '' ? null : actor.trim(),
            });
            onUse(article.id, article);
        } catch (caught) {
            const existing = existingArticle(caught);
            if (existing !== null && !existing.isArchived) onUse(existing.id, existing);
            else setError(toApiError(caught).message);
        } finally {
            setBusy(false);
        }
    };

    return (
        <div className="mt-1 space-y-2 rounded border border-gray-300 bg-gray-50 p-2 text-xs" data-tour="supply-new-article" data-tour-key={index}>
            <div className="flex items-center justify-between gap-2">
                <span className="font-semibold text-gray-900">{t('supply_new_article_title')}</span>
                <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} onClick={onClose}>{t('supply_new_article_close')}</button>
            </div>
            <label className="block font-medium text-gray-700">
                {t('supply_new_article_kind')} *
                <select value={kindKey} disabled={busy} onChange={(event) => { setKindKey(event.target.value); setError(null); }} className={inputClass} data-tour="supply-new-article-kind" data-tour-key={index}>
                    <option value="">{t('supply_new_article_kind_choose')}</option>
                    {kinds.map((candidate) => <option key={candidate.key} value={candidate.key}>{candidate.label}</option>)}
                </select>
                <span className="block font-normal text-gray-500">{t('supply_new_article_kind_hint')}</span>
            </label>
            <div className="grid grid-cols-2 gap-2">
                <div className="col-span-2">
                    <label htmlFor={`${prefix}.origin-input`} className="font-medium text-gray-700">{t('receipt_attr_origin')} *</label>
                    {countries === null
                        ? <p className="text-gray-600">{t('supply_open_loading')}</p>
                        : <CountryPicker id={`${prefix}.origin`} dataTour="supply-new-article-origin" dataTourKey={index} countries={countries} value={entered.origin} onChange={(origin) => edit({ origin })} disabled={busy} />}
                </div>
                {RECEIPT_ATTRIBUTES.map((attribute) => (
                    <label key={attribute} className="font-medium text-gray-700">
                        {t(LABEL_KEYS[attribute])} {t('field_optional')}
                        <input value={entered[attribute]} disabled={busy} maxLength={attribute === 'tradeClass' ? 50 : 100} placeholder={t('receipt_attr_placeholder')} className={inputClass}
                            onChange={(event) => edit({ [attribute]: event.target.value } as Partial<EnteredAttributes>)} data-tour={`supply-new-article-${attribute}`} data-tour-key={index} />
                    </label>
                ))}
                <label className="font-medium text-gray-700">
                    {t('supply_new_article_kg')} {t('field_optional')}
                    <input value={kg} inputMode="decimal" disabled={busy} className={inputClass} aria-invalid={perPackage !== null && !perPackage.ok}
                        onChange={(event) => { setKg(event.target.value); setError(null); }} data-tour="supply-new-article-kg" data-tour-key={index} />
                </label>
            </div>
            {kind !== null && entered.origin !== null && (
                <NewArticlePrompt articleName={kind.label} countryName={names.get(entered.origin) ?? null} entered={entered}
                    similar={similarVariants(kind.variants, entered, names)} onChoose={(variant) => onUse(variant.id, null)}
                    onCreate={() => void create()} busy={busy || (perPackage !== null && !perPackage.ok)} error={error} dataTourKey={`supply-${index}`} />
            )}
        </div>
    );
}
