'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { toApiError } from '../../services/apiClient';
import { deleteProduct, listEmptyProducts } from '../../services/articleService';
import type { EmptyProduct } from '../../types/article';
import { secondaryButtonClass } from '../supply/SuccessPanel';
import { useActorReference } from '../supply/useActorReference';

// Productsoorten zonder artikelen (bijvoorbeeld een tikfout in de productsoort): verwijderen kan, met dezelfde controle in de backend
// (409 zodra er intussen een artikel onder hangt). Verschijnt alleen als er zulke soorten zijn.
export default function EmptyProducts({ reloadToken, onDeleted }: { reloadToken: number; onDeleted: (name: string) => void }) {
    const { t } = useTranslation();
    const [actor] = useActorReference();
    const [products, setProducts] = useState<EmptyProduct[]>([]);
    const [busy, setBusy] = useState<number | null>(null);
    const [message, setMessage] = useState<string | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        listEmptyProducts(controller.signal).then(setProducts).catch(() => setProducts([]));
        return () => controller.abort();
    }, [reloadToken]);

    if (products.length === 0) return null;

    const remove = async (product: EmptyProduct) => {
        setBusy(product.id);
        setMessage(null);
        try {
            await deleteProduct(product.id, actor.trim() === '' ? null : actor.trim());
            setProducts((current) => current.filter((candidate) => candidate.id !== product.id));
            onDeleted(product.name);
        } catch (caught) {
            setMessage(toApiError(caught).message);
        } finally {
            setBusy(null);
        }
    };

    return (
        <section className="rounded-md border border-gray-200 bg-white p-3 text-sm" aria-label={t('articles_empty_products_title')} data-testid="empty-products">
            <h2 className="font-semibold text-gray-900">{t('articles_empty_products_title')}</h2>
            <p className="text-xs text-gray-600">{t('articles_empty_products_hint')}</p>
            <ul className="mt-2 divide-y divide-gray-100">
                {products.map((product) => (
                    <li key={product.id} className="flex items-center justify-between gap-2 py-1">
                        <span className="break-words text-gray-900">{product.name}</span>
                        <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} disabled={busy !== null}
                            onClick={() => remove(product)} aria-label={`${t('articles_delete')}: ${product.name}`}>{t('articles_delete')}</button>
                    </li>
                ))}
            </ul>
            {message !== null && <p role="alert" className="mt-1 text-xs font-medium text-red-800">{message}</p>}
        </section>
    );
}
