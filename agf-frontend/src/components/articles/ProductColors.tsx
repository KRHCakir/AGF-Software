'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getProductColors, updateProductColors } from '../../services/partijService';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Feedback 22 november: de kleurwaarden van een product (Stamgegevens), bijvoorbeeld groen en geel, of 1 tot 6 voor ananas. Gescheiden door
// komma's; leeg = geen kleur voor dit product. De lijst geldt voor alle artikelen van het product; QC kiest er de kleur van een partij uit.
export default function ProductColors({ productId }: { productId: number }) {
    const { t } = useTranslation();
    const [name, setName] = useState('');
    const [text, setText] = useState('');
    const [saved, setSaved] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        getProductColors(productId, controller.signal)
            .then((found) => { setName(found.productName); setText(found.colors.join(', ')); setSaved(found.colors.join(', ')); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [productId]);

    const save = async (event: FormEvent) => {
        event.preventDefault();
        if (busy) return;
        setBusy(true);
        setError(null);
        try {
            const result = await updateProductColors(productId, text.split(',').map((color) => color.trim()).filter((color) => color !== ''));
            setText(result.colors.join(', '));
            setSaved(result.colors.join(', '));
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };

    if (saved === null) return error ? <ErrorNotice error={error} /> : null;
    return (
        <form onSubmit={(event) => void save(event)} className="space-y-1 rounded-md border border-gray-200 p-3" data-testid="product-colors">
            <label htmlFor={`product-colors-${productId}`} className="text-sm font-semibold text-gray-900">{t('product_colors_title', { product: name })}</label>
            <div className="flex flex-wrap items-center gap-2">
                <input id={`product-colors-${productId}`} value={text} onChange={(event) => setText(event.target.value)} className={`${inputClass} max-w-md`}
                    placeholder={t('product_colors_placeholder')} data-tour="product-colors" />
                <button type="submit" className={secondaryButtonClass} disabled={busy || text === saved} data-tour="product-colors-save">{busy ? t('supply_saving') : t('product_colors_save')}</button>
            </div>
            <span className="block text-xs text-gray-500">{t('product_colors_hint')}</span>
            {error && <ErrorNotice error={error} />}
        </form>
    );
}
