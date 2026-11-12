'use client';

import Link from 'next/link';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatMoney } from '../../lib/availability';
import { formatDecimalInput } from '../../lib/decimal';
import { needsAttention } from '../../lib/aiProposal';
import type { AiExtraction } from '../../types/aiReading';
import OrderNumberText from './OrderNumberText';

// Boven het voorstel: welk model het las, of het nog niet klopt (geel), de leverancier (keuze bij meerdere treffers, "niet gevonden" met
// een link naar Relatie toevoegen, nooit zelf aanmaken), de inkooporder op het ordernummer van de leverancier (de gebruiker bevestigt),
// een dubbele factuur (vóór opslaan) en de totalen zoals op de factuur.
export default function AiReviewBanner({ extraction, vendorId, onVendor, linkOrderId, onLinkOrder }: {
    extraction: AiExtraction;
    vendorId: string;
    onVendor: (vendorId: number) => void;
    linkOrderId: number | null;
    onLinkOrder: (orderId: number | null) => void;
}) {
    const { t, locale } = useTranslation();
    const proposal = extraction.proposal;
    const matches = extraction.matches;
    const money = (value: number | null) => (value === null ? '–' : formatMoney(value, null, locale));
    // De voorgestelde order hoort bij één leverancier: staat er een andere leverancier, dan is er geen voorstel. De koppeling zelf haalt het
    // formulier weg bij een leverancierswissel (een gebruikersactie, niet bij elke weergave: het concept kan nog onderweg zijn).
    const suggestedOrder = matches?.orderCandidates.find((order) => order.id === matches.suggestedOrderId && String(order.vendorId) === vendorId) ?? null;
    const duplicate = matches?.duplicateInvoice && vendorId === String(matches.suggestedVendorId) ? matches.duplicateInvoice : null;

    return (
        <section className="space-y-1.5 rounded-md border border-blue-300 bg-blue-50 px-3 py-2 text-sm text-blue-950" data-testid="ai-review" data-tour="pinvoice-ai-review">
            <p className="font-semibold">
                {t('ai_read_by', { model: extraction.modelName ?? '–' })}
                {extraction.usedFallback && <span className="ml-1 font-normal">· {t('ai_read_by_fallback')}</span>}
            </p>
            {needsAttention(extraction)
                ? <p role="alert" className="rounded border border-amber-300 bg-amber-100 px-2 py-1 font-medium text-amber-950" data-testid="ai-attention">⚠ {t('ai_check_yellow')}</p>
                : <p>{t('ai_check_then_approve')}</p>}

            {matches && matches.suggestedVendorId === null && matches.vendorCandidates.length > 1 && (
                <p data-testid="ai-vendor-choice">
                    {t('ai_vendor_choose')}{' '}
                    {matches.vendorCandidates.map((candidate) => (
                        <button key={candidate.vendorId} type="button" onClick={() => onVendor(candidate.vendorId)}
                            className={`mr-1 rounded border px-2 py-0.5 ${vendorId === String(candidate.vendorId) ? 'border-blue-700 bg-white font-semibold' : 'border-blue-300 bg-white/60'}`}>
                            {candidate.name} <span className="text-xs text-gray-600">({candidate.matchedOn.map((m) => t(`ai_matched_${m}`)).join(', ')})</span>
                        </button>
                    ))}
                </p>
            )}
            {matches && matches.vendorCandidates.length === 0 && (
                <p data-testid="ai-vendor-missing">
                    {t('ai_vendor_not_found', { name: proposal?.vendorName ?? '–' })}{' '}
                    <Link href="/relations?new=1" target="_blank" className="font-medium text-blue-800 underline underline-offset-2">{t('ai_vendor_add')}</Link>
                </p>
            )}

            {suggestedOrder ? (
                <label className="flex flex-wrap items-center gap-2" data-testid="ai-order-link">
                    <input type="checkbox" checked={linkOrderId === suggestedOrder.id} onChange={(event) => onLinkOrder(event.target.checked ? suggestedOrder.id : null)} />
                    <span>{t('ai_order_link')}</span>
                    <span className="font-semibold"><OrderNumberText id={suggestedOrder.id} orderNumber={suggestedOrder.orderNumber} supplierReference={suggestedOrder.supplierReference} /></span>
                </label>
            ) : (
                <p className="text-gray-800" data-testid="ai-order-none">
                    {proposal?.supplierOrderNumber
                        ? t((matches?.orderCandidates.length ?? 0) > 1 ? 'ai_order_many' : 'ai_order_not_found', { number: proposal.supplierOrderNumber })
                        : t('ai_order_none')}
                </p>
            )}

            {duplicate && (
                <p role="alert" className="rounded border border-red-300 bg-red-50 px-2 py-1 text-red-950" data-testid="ai-duplicate">
                    {t('purchase_invoice_exists', { number: duplicate.invoiceNumber })}{' '}
                    <Link href={`/purchase-invoices?id=${duplicate.id}`} target="_blank" className="underline">{t('ai_duplicate_open')}</Link>
                </p>
            )}
            {proposal && (
                <p className="text-xs text-gray-700" data-testid="ai-document-totals">
                    {t('ai_document_totals', { net: money(proposal.totalNet), vat: money(proposal.totalVat), gross: money(proposal.totalGross), currency: proposal.currency ?? '' })}
                </p>
            )}
            {/* Korting op het totaal: het formulier kent alleen een korting per regel. Verdeeld: uitleg; niet precies te verdelen: geel, de gebruiker vult hem in. */}
            {proposal && proposal.documentDiscount !== null && (proposal.documentDiscountPercentage !== null
                ? <p className="text-xs text-gray-700" data-testid="ai-document-discount">
                    {t('ai_document_discount', { percentage: formatDecimalInput(proposal.documentDiscountPercentage, 2), amount: money(proposal.documentDiscount), currency: proposal.currency ?? '' })}
                </p>
                : <p role="alert" className="rounded border border-amber-300 bg-amber-100 px-2 py-1 text-xs text-amber-950" data-testid="ai-document-discount-manual">
                    ⚠ {t('ai_document_discount_manual', { amount: money(proposal.documentDiscount), currency: proposal.currency ?? '' })}
                </p>)}
        </section>
    );
}

// Gele markering en korte uitleg bij een veld waar de AI twijfelt of dat niet rekent.
export const aiFieldClass = (codes: string[] | undefined) => (codes && codes.length > 0 ? 'rounded bg-amber-50 ring-2 ring-amber-300' : '');

export function AiFieldHint({ codes }: { codes: string[] | undefined }) {
    const { t } = useTranslation();
    if (!codes || codes.length === 0) return null;
    return <span className="block text-xs font-medium text-amber-900" data-testid="ai-hint">{codes.map((code) => t(`ai_problem_${code}`)).join(' · ')}</span>;
}
