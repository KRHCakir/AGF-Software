'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateOnly, todayInAmsterdam } from '../../lib/calendarDate';
import { CreditDraft, insuranceExpired, insuranceExpiresSoon, insuranceGap, parseAmount } from '../../lib/roleTermsRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { getCustomerCredit } from '../../services/relationService';
import type { CreditExposure, RelationReference } from '../../types/relation';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Krediet van de klant (EUR): interne limiet en kredietverzekering (verzekeraar, verzekerde limiet, polis, einddatum). De
// kredietpositie komt van de server (openstaande facturen, geleverd-niet-gefactureerd, open orders); nieuwe orders zonder prijs
// worden alleen geteld, nooit geschat. Verlopen of bijna verlopen verzekering en een gat tussen limiet en dekking zijn
// waarschuwingen, geen fouten. Niets hier blokkeert een order.
export default function CustomerCreditSection({ customerId, credit, onChange, reference, errors, locked, refreshToken }: {
    customerId: number | null;
    credit: CreditDraft;
    onChange: (patch: Partial<CreditDraft>) => void;
    reference: RelationReference;
    errors: FieldErrors;
    locked: boolean;
    // Na opslaan opnieuw ophalen (limiet of verzekering kan gewijzigd zijn).
    refreshToken: number;
}) {
    const { t, locale } = useTranslation();
    const [exposure, setExposure] = useState<CreditExposure | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [reloadToken, setReloadToken] = useState(0);
    const e = (field: string) => errors[`customer.${field}`];
    const today = todayInAmsterdam();
    const money = (value: number) => new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { style: 'currency', currency: 'EUR' }).format(value);

    useEffect(() => {
        if (customerId === null) return;
        const controller = new AbortController();
        getCustomerCredit(customerId, controller.signal)
            .then((data) => { setExposure(data); setLoadError(null); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, [customerId, refreshToken, reloadToken]);

    // Live uit de invoer (zelfde vensters als de server): verlopen, verloopt binnen 30 dagen, gat tussen limiet en dekking.
    const insuredUntil = credit.insuredUntil === '' ? null : credit.insuredUntil;
    const expired = credit.isInsured && insuranceExpired(insuredUntil, today);
    const soon = credit.isInsured && insuranceExpiresSoon(insuredUntil, today);
    const limit = parseAmount(credit.creditLimit);
    const insured = parseAmount(credit.insuredLimit);
    const gap = insuranceGap(limit ?? null, credit.isInsured, insured ?? null);

    const amountInput = (field: 'creditLimit' | 'insuredLimit', labelKey: string, required = false) => (
        <div>
            <label htmlFor={`customer.${field}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}{required ? ' *' : ''}</label>
            <input {...fieldProps(`customer.${field}`, e(field))} inputMode="decimal" value={credit[field]} maxLength={20} disabled={locked} placeholder="0,00"
                onChange={(event) => onChange({ [field]: event.target.value })} className={inputClass} />
            <FieldError id={`customer.${field}`} messages={e(field)} />
        </div>
    );

    return (
        <div className="space-y-2" data-testid="customer-credit">
            <p className="text-xs font-medium text-gray-700">{t('relations_credit_title')}</p>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-4">
                {amountInput('creditLimit', 'relations_credit_limit')}
                <label className="flex items-end gap-1 pb-1 text-sm text-gray-900">
                    <input type="checkbox" id="customer.isInsured-input" checked={credit.isInsured} disabled={locked} onChange={(event) => onChange({ isInsured: event.target.checked })} />
                    {t('relations_credit_insured')}
                </label>
                {credit.isInsured && (
                    <>
                        <div>
                            <label htmlFor="customer.insurer-input" className="text-xs font-medium text-gray-700">{t('relations_credit_insurer')} *</label>
                            <select {...fieldProps('customer.insurer', e('insurer'))} value={credit.insurer} disabled={locked} className={inputClass} onChange={(event) => onChange({ insurer: event.target.value })}>
                                <option value="">–</option>
                                {reference.creditInsurers.map((insurer) => <option key={insurer} value={insurer}>{t(`relations_insurer_${insurer}`)}</option>)}
                            </select>
                            <FieldError id="customer.insurer" messages={e('insurer')} />
                        </div>
                        {credit.insurer === 'Other' && (
                            <div>
                                <label htmlFor="customer.insurerText-input" className="text-xs font-medium text-gray-700">{t('relations_credit_insurer_text')} *</label>
                                <input {...fieldProps('customer.insurerText', e('insurerText'))} value={credit.insurerText} maxLength={100} disabled={locked} className={inputClass} onChange={(event) => onChange({ insurerText: event.target.value })} />
                                <FieldError id="customer.insurerText" messages={e('insurerText')} />
                            </div>
                        )}
                        {amountInput('insuredLimit', 'relations_credit_insured_limit', true)}
                        <div>
                            <label htmlFor="customer.insurerReference-input" className="text-xs font-medium text-gray-700">{t('relations_credit_reference')}</label>
                            <input {...fieldProps('customer.insurerReference', e('insurerReference'))} value={credit.insurerReference} maxLength={64} disabled={locked} className={`${inputClass} font-mono`} onChange={(event) => onChange({ insurerReference: event.target.value })} />
                            <FieldError id="customer.insurerReference" messages={e('insurerReference')} />
                        </div>
                        <div>
                            <label htmlFor="customer.insuredUntil-input" className="text-xs font-medium text-gray-700">{t('relations_credit_insured_until')}</label>
                            <input {...fieldProps('customer.insuredUntil', e('insuredUntil'))} type="date" value={credit.insuredUntil} disabled={locked} className={inputClass} onChange={(event) => onChange({ insuredUntil: event.target.value })} />
                            {expired && <span className="block text-xs font-medium text-red-800" data-testid="credit-insurance-expired">{t('relations_credit_insurance_expired')}</span>}
                            {soon && <span className="block text-xs font-medium text-amber-800" data-testid="credit-insurance-expires-soon">{t('relations_credit_insurance_expires_soon')}</span>}
                        </div>
                    </>
                )}
            </div>
            {gap !== null && <p className="text-xs font-medium text-amber-800" data-testid="credit-insurance-gap">{t('relations_credit_gap', { gap: money(gap) })}</p>}

            {customerId !== null && (
                <div className="rounded border border-gray-200 bg-gray-50 px-2 py-2 text-xs text-gray-800" data-testid="customer-credit-exposure">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                        <p className="font-medium">{t('relations_credit_exposure_title')}{exposure ? ` · ${t('relations_credit_as_of', { date: formatDateOnly(exposure.asOf, locale) })}` : ''}</p>
                        <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} onClick={() => setReloadToken((n) => n + 1)}>↻ {t('availability_refresh')}</button>
                    </div>
                    {loadError && <p role="alert" className="mt-1 text-red-800">{t('relations_credit_load_error')}</p>}
                    {exposure && (
                        <>
                            <dl className="mt-1 grid grid-cols-2 gap-x-4 gap-y-1 sm:grid-cols-3">
                                <div><dt className="text-gray-600">{t('relations_credit_limit')}</dt><dd className="font-medium">{exposure.creditLimit === null ? t('relations_credit_no_limit') : money(exposure.creditLimit)}</dd></div>
                                <div><dt className="text-gray-600">{t('relations_credit_insured_limit')}</dt><dd className="font-medium">{exposure.isInsured && exposure.insuredLimit !== null ? money(exposure.insuredLimit) : '–'}</dd></div>
                                <div><dt className="text-gray-600">{t('relations_credit_outstanding')}</dt><dd className="font-medium" data-testid="credit-outstanding">{money(exposure.totalOutstanding)}</dd></div>
                                <div><dt className="text-gray-600">{t('relations_credit_open_invoices', { count: String(exposure.openInvoicesCount) })}</dt><dd>{money(exposure.openInvoicesAmount)}</dd></div>
                                <div><dt className="text-gray-600">{t('relations_credit_delivered', { count: String(exposure.deliveredNotInvoicedCount) })}</dt><dd>{money(exposure.deliveredNotInvoicedAmount)}</dd></div>
                                <div><dt className="text-gray-600">{t('relations_credit_open_orders', { count: String(exposure.openOrdersCount) })}</dt><dd>{money(exposure.openOrdersAmount)}</dd></div>
                            </dl>
                            {exposure.isOverLimit && <p className="mt-1 font-medium text-red-800" data-testid="credit-over-limit">{t('relations_credit_over_limit', { amount: money(exposure.overLimitAmount) })}</p>}
                            {exposure.insuranceGap !== null && <p className="mt-1 font-medium text-amber-800">{t('relations_credit_gap', { gap: money(exposure.insuranceGap) })}</p>}
                            {exposure.insuranceExpired && <p className="mt-1 font-medium text-red-800">{t('relations_credit_insurance_expired')}</p>}
                            {exposure.insuranceExpiresWithin30Days && <p className="mt-1 font-medium text-amber-800">{t('relations_credit_insurance_expires_soon')}</p>}
                            {exposure.unpricedSalesOrdersCount > 0 && (
                                <p className="mt-1 text-gray-700" data-testid="credit-unpriced">
                                    {t(exposure.unpricedSalesOrdersCount === 1 ? 'relations_credit_unpriced_one' : 'relations_credit_unpriced', { count: String(exposure.unpricedSalesOrdersCount) })}
                                </p>
                            )}
                        </>
                    )}
                </div>
            )}
        </div>
    );
}
