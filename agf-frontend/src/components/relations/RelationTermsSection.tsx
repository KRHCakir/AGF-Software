'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateOnly, todayInAmsterdam } from '../../lib/calendarDate';
import { conditionDiscountDate, conditionDueDate } from '../../lib/paymentCondition';
import type { RoleTermsDraft } from '../../lib/roleTermsRules';
import type { PaymentConditionSummary } from '../../types/paymentCondition';
import type { RelationReference } from '../../types/relation';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';

// Rolafspraken van één rol (klant of leverancier): betalingsconditie (alleen actieve kiesbaar; een inactieve huidige conditie blijft
// zichtbaar en gekozen), betaalwijze met mandaat bij incasso, valuta, leveringsconditie (Incoterm + plaats of eigen omschrijving) en
// handelsvoorwaarden. Velden die bij de keuze niet horen verdwijnen uit beeld en worden bij opslaan leeggemaakt (zelfde regel als de server).
export default function RelationTermsSection({ prefix, terms, onChange, conditions, currentCondition, reference, errors, locked }: {
    prefix: 'customer' | 'vendor';
    terms: RoleTermsDraft;
    onChange: (patch: Partial<RoleTermsDraft>) => void;
    conditions: readonly PaymentConditionSummary[];
    currentCondition: PaymentConditionSummary | null;
    reference: RelationReference;
    errors: FieldErrors;
    locked: boolean;
}) {
    const { t, locale } = useTranslation();
    const k = (field: string) => `${prefix}.${field}`;
    const e = (field: string) => errors[k(field)];
    const currencyField = prefix === 'customer' ? 'currency' : 'defaultCurrency';
    const description = (c: PaymentConditionSummary) => `${c.code} · ${locale === 'en' ? c.descriptionEn : c.descriptionNl}`;

    // Keuzelijst: actieve condities plus de huidige (ook als zij inactief is of niet in de lijst staat), nooit een andere inactieve.
    const options = currentCondition !== null && !conditions.some((c) => c.id === currentCondition.id) ? [...conditions, currentCondition] : [...conditions];
    const chosen = options.find((c) => String(c.id) === terms.paymentConditionId) ?? null;
    const today = todayInAmsterdam();
    const due = chosen === null ? null : conditionDueDate(today, chosen.days, chosen.basis);
    const discount = chosen === null ? null : conditionDiscountDate(today, chosen);

    const label = (field: string, labelKey: string, required = false) => (
        <label htmlFor={`${k(field)}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}{required ? ' *' : ''}</label>
    );
    const input = (field: keyof RoleTermsDraft & string, labelKey: string, options: { maxLength?: number; type?: string; required?: boolean; mono?: boolean; list?: string; errorField?: string; span?: string } = {}) => (
        <div className={options.span ?? ''}>
            <label htmlFor={`${k(options.errorField ?? field)}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}{options.required ? ' *' : ''}</label>
            <input {...fieldProps(k(options.errorField ?? field), e(options.errorField ?? field))} type={options.type ?? 'text'} value={terms[field]} maxLength={options.maxLength ?? 200} list={options.list} disabled={locked}
                onChange={(event) => onChange({ [field]: event.target.value } as Partial<RoleTermsDraft>)} className={`${inputClass}${options.mono ? ' font-mono uppercase' : ''}`} />
            <FieldError id={k(options.errorField ?? field)} messages={e(options.errorField ?? field)} />
        </div>
    );

    return (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-4" data-testid={`${prefix}-terms`}>
            <div className="sm:col-span-2">
                {label('paymentConditionId', 'relations_terms_condition')}
                <select {...fieldProps(k('paymentConditionId'), e('paymentConditionId'))} value={terms.paymentConditionId} disabled={locked} className={inputClass}
                    onChange={(event) => onChange({ paymentConditionId: event.target.value })}>
                    {(terms.paymentConditionId === '' || chosen === null) && <option value="">{t('relations_terms_condition_default')}</option>}
                    {options.map((c) => <option key={c.id} value={String(c.id)}>{description(c)}{c.isActive ? '' : ` (${t('relations_inactive')})`}</option>)}
                </select>
                {chosen !== null && !chosen.isActive && <span className="block text-xs text-amber-800">{t('relations_terms_condition_inactive')}</span>}
                {chosen !== null && due !== null && (
                    <span className="block text-xs text-gray-500" data-testid={`${prefix}-terms-example`}>
                        {t('relations_terms_condition_example', { invoice: formatDateOnly(today, locale), due: formatDateOnly(due, locale) })}
                        {discount !== null && chosen.discountPercent !== null && <> · {t('relations_terms_discount_example', { percent: String(chosen.discountPercent), date: formatDateOnly(discount, locale) })}</>}
                    </span>
                )}
                <FieldError id={k('paymentConditionId')} messages={e('paymentConditionId')} />
            </div>
            <div>
                {label('paymentMethod', 'relations_terms_payment_method')}
                <select {...fieldProps(k('paymentMethod'), e('paymentMethod'))} value={terms.paymentMethod || 'BankTransfer'} disabled={locked} className={inputClass}
                    onChange={(event) => onChange({ paymentMethod: event.target.value })}>
                    {reference.paymentMethods.map((method) => <option key={method} value={method}>{t(`relations_payment_method_${method}`)}</option>)}
                </select>
                <FieldError id={k('paymentMethod')} messages={e('paymentMethod')} />
            </div>
            {input('currency', 'relations_field_currency', { maxLength: 3, mono: true, list: 'relation-currencies', errorField: currencyField })}
            {terms.paymentMethod === 'DirectDebit' && (
                <>
                    {input('mandateReference', 'relations_terms_mandate_reference', { maxLength: 35, required: true, span: 'sm:col-span-1' })}
                    {input('mandateSignedOn', 'relations_terms_mandate_signed_on', { type: 'date', required: true })}
                </>
            )}
            <div>
                {label('deliveryTermKind', 'relations_terms_delivery')}
                <select {...fieldProps(k('deliveryTermKind'), e('deliveryTermKind'))} value={terms.deliveryTermKind} disabled={locked} className={inputClass}
                    onChange={(event) => onChange({ deliveryTermKind: event.target.value })}>
                    <option value="">–</option>
                    {reference.deliveryTermKinds.map((kind) => <option key={kind} value={kind}>{t(`relations_delivery_${kind}`)}</option>)}
                </select>
                <FieldError id={k('deliveryTermKind')} messages={e('deliveryTermKind')} />
            </div>
            {terms.deliveryTermKind === 'Incoterm' && (
                <>
                    <div>
                        {label('incoterm', 'relations_terms_incoterm', true)}
                        <select {...fieldProps(k('incoterm'), e('incoterm'))} value={terms.incoterm.toUpperCase()} disabled={locked} className={inputClass} onChange={(event) => onChange({ incoterm: event.target.value })}>
                            <option value="">–</option>
                            {reference.incoterms.map((code) => <option key={code} value={code}>{code}</option>)}
                        </select>
                        <FieldError id={k('incoterm')} messages={e('incoterm')} />
                    </div>
                    {input('incotermPlace', 'relations_terms_incoterm_place', { maxLength: 100, required: true })}
                </>
            )}
            {terms.deliveryTermKind !== '' && terms.deliveryTermKind !== 'Incoterm' && input('deliveryTermText', terms.deliveryTermKind === 'Other' ? 'relations_terms_delivery_text_required' : 'relations_terms_delivery_text', { maxLength: 200, required: terms.deliveryTermKind === 'Other', span: 'sm:col-span-2' })}
            <div>
                {label('tradeConditions', 'relations_terms_trade')}
                <select {...fieldProps(k('tradeConditions'), e('tradeConditions'))} value={terms.tradeConditions} disabled={locked} className={inputClass}
                    onChange={(event) => onChange({ tradeConditions: event.target.value })}>
                    <option value="">–</option>
                    {reference.tradeConditions.map((kind) => <option key={kind} value={kind}>{t(`relations_trade_${kind}`)}</option>)}
                </select>
                <FieldError id={k('tradeConditions')} messages={e('tradeConditions')} />
            </div>
            {terms.tradeConditions === 'Other' && input('tradeConditionsText', 'relations_terms_trade_text', { maxLength: 200, required: true, span: 'sm:col-span-2' })}
        </div>
    );
}
