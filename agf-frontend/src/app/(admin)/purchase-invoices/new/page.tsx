'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { ErrorState, LoadState, LoadingRows } from '../../../../components/availability/StateViews';
import PurchaseInvoiceForm from '../../../../components/purchase/PurchaseInvoiceForm';
import AiDocumentViewer from '../../../../components/purchase/AiDocumentViewer';
import AiUploadPanel from '../../../../components/purchase/AiUploadPanel';
import { draftFromProposal } from '../../../../lib/aiProposal';
import { type InvoiceDraft, emptyDraft, isBlankLine } from '../../../../lib/purchaseInvoiceRules';
import { type StoredAiReview, clearAiReview, clearDraft, loadAiReview, loadDraft, saveAiReview } from '../../../../lib/purchaseInvoiceStore';
import { todayInAmsterdam } from '../../../../lib/calendarDate';
import { closeAiExtraction } from '../../../../services/aiReadingService';
import type { AiExtraction } from '../../../../types/aiReading';
import PurchaseInvoiceSettingsPanel from '../../../../components/purchase/PurchaseInvoiceSettingsPanel';
import { useTranslation } from '../../../../i18n/LanguageContext';
import { toApiError } from '../../../../services/apiClient';
import { getPurchaseInvoiceSettings, getPurchaseVendors } from '../../../../services/purchaseInvoiceService';
import { getProductsWithVariants } from '../../../../services/referenceDataService';
import { listCostArticles } from '../../../../services/costArticleService';
import type { CostArticle } from '../../../../types/costArticle';
import type { ProductOption } from '../../../../types/referenceData';
import { MissingMasterDataNotice } from '../../../../components/shared/FirstStep';
import { missingMasterData } from '../../../../lib/masterDataStatus';
import type { PurchaseInvoiceSettings, PurchaseVendor } from '../../../../types/purchaseInvoice';

interface Reference {
    vendors: PurchaseVendor[];
    products: ProductOption[];
    costArticles: CostArticle[];
    settings: PurchaseInvoiceSettings;
}

// Inkoopfactuur invoeren: één compact scherm. Referentiedata (leveranciers, artikelen, instellingen) wordt los van het
// concept geladen en ververst: het formulier bewaart zijn eigen waarden en wordt daardoor nooit overschreven.
// Factuur uploaden (stap 7a): de AI leest het document en vult het formulier voor; links het document, rechts het voorstel (op een
// telefoon het document erboven). Goedkeuren slaat op via het gewone opslagpad; Afwijzen gooit het voorstel weg; Handmatig verder houdt
// de waarden als gewoon concept zonder AI-goedkeuring.
export default function NewPurchaseInvoicePage() {
    const { t } = useTranslation();
    const [state, setState] = useState<LoadState<Reference>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);
    const [settingsOpen, setSettingsOpen] = useState(false);
    const [review, setReview] = useState<StoredAiReview | null>(() => loadAiReview());
    const [seed, setSeed] = useState<{ key: number; draft: InvoiceDraft } | null>(null);

    const changeReview = (next: StoredAiReview | null) => {
        setReview(next);
        if (next === null) clearAiReview(); else saveAiReview(next);
    };
    const hasDraft = () => {
        const current = loadDraft();
        return current !== null && (current.header.vendorId !== '' || current.header.invoiceNumber.trim() !== '' || current.lines.some((line) => !isBlankLine(line)));
    };
    const propose = (extraction: AiExtraction, data: Reference) => {
        if (review !== null) void closeAiExtraction(review.extraction.id, 'Rejected').catch(() => undefined);
        setSeed({ key: Date.now(), draft: draftFromProposal(extraction, data.settings, data.vendors, todayInAmsterdam()) });
        changeReview({ extraction, linkOrderId: extraction.matches?.suggestedOrderId ?? null });
    };
    const close = (outcome: 'Rejected' | 'Manual', data: Reference) => {
        if (review === null) return;
        void closeAiExtraction(review.extraction.id, outcome).catch(() => undefined);
        if (outcome === 'Rejected') {
            clearDraft();
            setSeed({ key: Date.now(), draft: emptyDraft(todayInAmsterdam(), data.settings) });
        }
        changeReview(null);
    };

    useEffect(() => {
        const controller = new AbortController();
        // Kostenartikelen zijn een hulp in de artikelkeuze: zonder antwoord blijft het scherm bruikbaar (vrije omschrijving).
        Promise.all([getPurchaseVendors(controller.signal), getProductsWithVariants(controller.signal), getPurchaseInvoiceSettings(controller.signal),
            listCostArticles(false, controller.signal).catch(() => [] as CostArticle[])])
            .then(([vendors, products, settings, costArticles]) => setState({ status: 'ready', data: { vendors, products, settings, costArticles } }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [reloadToken]);

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('purchase_new_title')}</h1>
                    <p className="text-sm text-gray-600">{t('purchase_new_subtitle')}</p>
                    {/* Een factuur heeft geen inkooporder of ontvangst nodig: ook kosten van derden gaan hier direct in. */}
                    <p className="text-sm text-gray-800" data-testid="purchase-direct-hint" data-tour="purchase-direct-hint">
                        {t('purchase_direct_hint')} <Link href="/settings#kostenartikelen" className="text-blue-800 underline underline-offset-2">{t('cost_articles_manage')}</Link>
                    </p>
                </div>
                <div className="flex items-center gap-2 text-sm">
                    <Link href="/purchase-invoices" className="font-medium text-blue-800 underline underline-offset-2">{t('purchase_back_to_list')}</Link>
                    {/* Discreet tandwiel: bedrijfsinstellingen, nooit naast Opslaan. */}
                    <button type="button" tabIndex={-1} aria-label={t('purchase_settings_title')} title={t('purchase_settings_title')} onClick={() => setSettingsOpen((open) => !open)}
                        className="rounded border border-transparent px-2 py-1 text-gray-500 hover:border-gray-300 hover:text-gray-800">⚙</button>
                </div>
            </div>

            {state.status === 'loading' && <LoadingRows label={t('purchase_loading')} />}
            {state.status === 'error' && <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {state.status === 'ready' && (
                <>
                    {settingsOpen && (
                        <PurchaseInvoiceSettingsPanel settings={state.data.settings} onClose={() => setSettingsOpen(false)}
                            onSaved={(settings) => setState({ status: 'ready', data: { ...state.data, settings } })} />
                    )}
                    <MissingMasterDataNotice missing={missingMasterData({ vendors: state.data.vendors }, ['vendor'])} />
                    {review === null && <AiUploadPanel onProposal={(extraction) => propose(extraction, state.data)} beforeUpload={() => !hasDraft() || window.confirm(t('ai_replace_confirm'))} />}
                    <div className={review === null ? '' : 'grid grid-cols-1 gap-3 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]'}>
                        {review !== null && <AiDocumentViewer key="document" extraction={review.extraction} />}
                        <div key="form" className="min-w-0">
                            <PurchaseInvoiceForm vendors={state.data.vendors} products={state.data.products} costArticles={state.data.costArticles} settings={state.data.settings} seed={seed}
                                onSaved={() => changeReview(null)}
                                ai={review === null ? null : {
                                    extraction: review.extraction, linkOrderId: review.linkOrderId,
                                    onLinkOrder: (linkOrderId) => changeReview({ ...review, linkOrderId }),
                                    onReject: () => close('Rejected', state.data), onManual: () => close('Manual', state.data),
                                }} />
                        </div>
                    </div>
                </>
            )}
        </div>
    );
}
