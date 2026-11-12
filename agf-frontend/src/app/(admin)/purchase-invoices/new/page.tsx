'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { ErrorState, LoadState, LoadingRows } from '../../../../components/availability/StateViews';
import PurchaseInvoiceForm from '../../../../components/purchase/PurchaseInvoiceForm';
import AiDocumentViewer from '../../../../components/purchase/AiDocumentViewer';
import AiUploadPanel from '../../../../components/purchase/AiUploadPanel';
import { draftFromProposal } from '../../../../lib/aiProposal';
import { type InvoiceDraft, emptyDraft, isBlankLine, selectVendor } from '../../../../lib/purchaseInvoiceRules';
import { addOrderLines } from '../../../../lib/invoiceOrderLink';
import { getPurchaseOrder, listInvoiceCandidates } from '../../../../services/purchaseOrderService';
import { type StoredAiReview, clearAiReview, clearDraft, loadAiReview, loadDraft, loadPending, saveAiReview } from '../../../../lib/purchaseInvoiceStore';
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
    const [closing, setClosing] = useState(false);
    const [closeFailed, setCloseFailed] = useState(false);

    const changeReview = (next: StoredAiReview | null) => {
        setReview(next);
        if (next === null) clearAiReview(); else saveAiReview(next);
    };
    // Een concept telt als ingevuld zodra iets afwijkt van een leeg concept: ook een datum, valuta, betaaltermijn, notitie of extra veld.
    const hasDraft = (settings: PurchaseInvoiceSettings) => {
        const current = loadDraft();
        if (current === null) return false;
        const blank = emptyDraft(todayInAmsterdam(), settings).header;
        const { extra, ...header } = current.header;
        const changed = (Object.keys(header) as (keyof typeof header)[]).some((field) => String(header[field] ?? '').trim() !== String(blank[field] ?? '').trim());
        return changed || Object.values(extra ?? {}).some((value) => String(value ?? '').trim() !== '') || current.lines.some((line) => !isBlankLine(line));
    };
    const propose = (extraction: AiExtraction, data: Reference) => {
        const draft = draftFromProposal(extraction, data.settings, data.vendors, todayInAmsterdam());
        setSeed({ key: Date.now(), draft });
        changeReview({ extraction, linkOrderId: extraction.matches?.suggestedOrderId ?? null, lineKeys: (extraction.proposal?.lines.length ?? 0) > 0 ? draft.lines.map((line) => line.key) : [] });
    };
    // Afwijzen en Handmatig verder: pas als de server de lezing heeft afgesloten verdwijnt het voorstel. Lukt dat niet, dan blijft alles
    // staan met een melding, en kan de gebruiker het opnieuw proberen.
    const close = async (outcome: 'Rejected' | 'Manual', data: Reference) => {
        if (review === null || closing) return;
        setClosing(true);
        setCloseFailed(false);
        try {
            await closeAiExtraction(review.extraction.id, outcome);
        } catch {
            setCloseFailed(true);
            return;
        } finally {
            setClosing(false);
        }
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

    // "Factuur invoeren" vanaf de order (stap 4b, ?orderId=): de leverancier en de open regels van die bon, met de prijs op de bon. Niet
    // tijdens een AI-voorstel of een verstuurde opslag zonder bevestiging; daarna gaat de parameter uit de adresbalk (verversen vult niet
    // opnieuw voor).
    const ready = state.status === 'ready' ? state.data : null;
    useEffect(() => {
        if (ready === null || review !== null || loadPending() !== null) return;
        const orderId = Number(new URLSearchParams(window.location.search).get('orderId'));
        if (!Number.isInteger(orderId) || orderId <= 0) return;
        const controller = new AbortController();
        getPurchaseOrder(orderId, controller.signal)
            .then(async (order) => {
                const vendor = ready.vendors.find((candidate) => candidate.id === order.vendorId) ?? null;
                if (vendor === null) return;
                const [found] = await listInvoiceCandidates(vendor.id, '', orderId, controller.signal);
                if (!found) return;
                const start = selectVendor(emptyDraft(todayInAmsterdam(), ready.settings), vendor, ready.settings);
                clearDraft();
                setSeed({ key: Date.now(), draft: addOrderLines(start, found, ready.settings.defaultVatPercentage) });
                window.history.replaceState(window.history.state, '', window.location.pathname);
            })
            .catch(() => undefined);
        return () => controller.abort();
        // eslint-disable-next-line react-hooks/exhaustive-deps -- eenmaal zodra de referentiedata er is
    }, [ready === null]);

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
                    {review === null && <AiUploadPanel onProposal={(extraction) => propose(extraction, state.data)} beforeUpload={() => !hasDraft(state.data.settings) || window.confirm(t('ai_replace_confirm'))} />}
                    {closeFailed && <p role="alert" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-950" data-testid="ai-close-failed">{t('ai_close_failed')}</p>}
                    <div className={review === null ? '' : 'grid grid-cols-1 gap-3 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]'}>
                        {review !== null && <AiDocumentViewer key="document" extraction={review.extraction} />}
                        <div key="form" className="min-w-0">
                            <PurchaseInvoiceForm vendors={state.data.vendors} products={state.data.products} costArticles={state.data.costArticles} settings={state.data.settings} seed={seed} busy={closing}
                                onSaved={() => changeReview(null)}
                                ai={review === null ? null : {
                                    extraction: review.extraction, lineKeys: review.lineKeys, linkOrderId: review.linkOrderId,
                                    onLinkOrder: (linkOrderId) => changeReview({ ...review, linkOrderId }),
                                    onReject: () => void close('Rejected', state.data), onManual: () => void close('Manual', state.data),
                                }} />
                        </div>
                    </div>
                </>
            )}
        </div>
    );
}
