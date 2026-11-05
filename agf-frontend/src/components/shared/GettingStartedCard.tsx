'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { gettingStarted } from '../../lib/masterDataStatus';
import { listArticles } from '../../services/articleService';
import { listPurchaseOrders } from '../../services/purchaseOrderService';
import { listRelations } from '../../services/relationService';

// Kleine Beginnen-kaart op de startpagina zolang er nog geen relaties of artikelen zijn: 1 relaties, 2 artikelen, 3 inkooporder.
// Verdwijnt vanzelf zodra die er zijn. Net als bij "Eerst nodig" tellen alleen actieve relaties en niet-gearchiveerde artikelen. Lukt
// het lezen niet, dan staat er gewoon niets (de startpagina werkt zonder).
export default function GettingStartedCard() {
    const { t } = useTranslation();
    const [counts, setCounts] = useState<{ relations: number; articles: number; orders: number } | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([
            listRelations({ role: 'all', query: '', includeInactive: false, page: 1, pageSize: 1 }, controller.signal),
            listArticles(controller.signal),
            listPurchaseOrders({ query: '', status: 'all', vendorId: null, page: 1, pageSize: 1 }, controller.signal),
        ])
            .then(([relations, articles, orders]) => setCounts({ relations: relations.total, articles: articles.filter((article) => !article.isArchived).length, orders: orders.total }))
            .catch(() => { /* geen kaart */ });
        return () => controller.abort();
    }, []);

    if (counts === null) return null;
    const card = gettingStarted(counts);
    if (!card.show) return null;
    return (
        <section aria-labelledby="start-getting-started" className="rounded-lg border border-blue-200 bg-blue-50/60 p-4" data-testid="getting-started">
            <h2 id="start-getting-started" className="text-base font-semibold text-gray-900">{t('start_begin_title')}</h2>
            <p className="text-sm text-gray-700">{t('start_begin_intro')}</p>
            <ol className="mt-2 space-y-1 text-sm">
                {card.steps.map((step, index) => (
                    <li key={step.id} className="flex flex-wrap items-center gap-2" data-testid={`getting-started-${step.id}`} data-done={step.done ? 'yes' : 'no'}>
                        <span aria-hidden="true" className={`inline-flex h-5 w-5 items-center justify-center rounded-full text-xs font-semibold ${step.done ? 'bg-green-600 text-white' : 'bg-white text-blue-900 ring-1 ring-blue-300'}`}>
                            {step.done ? '✓' : index + 1}
                        </span>
                        <Link href={step.href} className="font-medium text-blue-800 underline-offset-2 hover:underline">{t(`start_begin_${step.id}`)}</Link>
                        {step.done && <span className="text-xs text-green-800">{t('start_begin_done')}</span>}
                    </li>
                ))}
            </ol>
        </section>
    );
}
