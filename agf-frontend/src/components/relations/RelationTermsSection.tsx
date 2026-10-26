'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateOnly, todayInAmsterdam } from '../../lib/calendarDate';
import { freeTermsPreview, validateFreeTerms } from '../../lib/paymentCondition';
import type { RoleTermsDraft } from '../../lib/roleTermsRules';
import type { PaymentConditionSummary } from '../../types/paymentCondition';
import type { RelationReference } from '../../types/relation';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';

// Rolafspraken van één rol (klant of leverancier): vrije betalingstermijn (dagen, leeg = standaard als grijze placeholder, vinkje
// "na einde maand", optionele korting % + dagen), betaalwijze met mandaat bij incasso, valuta, leveringsconditie (Incoterm + plaats of
// eigen omschrijving) en handelsvoorwaarden. De server zoekt of maakt de passende betalingsconditie; codes ziet de gebruiker hier niet.
// Getallen zijn tekstvelden met inputMode="numeric": geen pijltje-omhoog/omlaag of scrollwiel dat 30 stil in 31 verandert, en de cursor-
// positie blijft leesbaar voor de toetsenbordnavigatie (type="number" geeft geen selectionStart). Velden die bij de keuze niet horen
// verdwijnen uit beeld en worden bij opslaan leeggemaakt (zelfde regel als de server).
export default function RelationTermsSection({ prefix, terms, onChange, currentCondition, reference, errors, locked }: {
    prefix: 'customer' | 'vendor';
    terms: RoleTermsDraft;
    onChange: (patch: Partial<RoleTermsDraft>) => void;
    // De opgeslagen conditie van de rol (alleen om een inactieve te melden zolang de termijn niet wijzigt).
    currentCondition: PaymentConditionSummary | null;
    reference: RelationReference;
    errors: FieldErrors;
    locked: boolean;
}) {
    const { t, locale } = useTranslation();
    const k = (field: string) => `${prefix}.${field}`;
    const e = (field: string) => errors[k(field)];
    const currencyField = prefix === 'customer' ? 'currency' : 'defaultCurrency';
    const defaultDays = reference.defaultPaymentTermsDays;
    const today = todayInAmsterdam();
    const preview = freeTermsPreview(today, terms, defaultDays);
    // Inactieve huidige conditie: alleen melden zolang de termijn er nog precies mee overeenkomt (dan blijft zij gekoppeld).
    const free = validateFreeTerms(terms, defaultDays);
    const keepsInactive = currentCondition !== null && !currentCondition.isActive && free.errors.length === 0 && currentCondition.days === free.effectiveDays
        && (currentCondition.basis === 'EndOfMonth') === terms.paymentTermsEndOfMonth
        && currentCondition.discountPercent === free.values.discountPercent && currentCondition.discountDays === free.values.discountDays;
    // Getalveld: tekst met numeriek toetsenbord; data-select-on-focus laat useKeyboardFlow de inhoud selecteren zodat je kunt overtypen.
    const numberInput = (field: 'paymentTermsDays' | 'discountPercent' | 'discountDays', labelKey: string, extra: { placeholder?: string; decimal?: boolean; width?: string } = {}) => (
        <div className={extra.width ?? ''}>
            <label htmlFor={`${k(field)}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}</label>
            <input {...fieldProps(k(field), e(field))} type="text" inputMode={extra.decimal ? 'decimal' : 'numeric'} autoComplete="off" data-select-on-focus="" value={terms[field]}
                placeholder={extra.placeholder} maxLength={extra.decimal ? 6 : 3} disabled={locked} onChange={(event) => onChange({ [field]: event.target.value } as Partial<RoleTermsDraft>)}
                className={`${inputClass} text-right tabular-nums placeholder:text-gray-400`} />
            <FieldError id={k(field)} messages={e(field)} />
        </div>
    );

    const label = (field: string, labelKey: string, required = false) => (
        <label htmlFor={`${k(field)}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}{required ? ' *' : ''}</label>
    );
    const input = (field: Exclude<keyof RoleTermsDraft, 'paymentTermsEndOfMonth'>, labelKey: string, options: { maxLength?: number; type?: string; required?: boolean; mono?: boolean; list?: string; errorField?: string; span?: string } = {}) => (
        <div className={options.span ?? ''}>
            <label htmlFor={`${k(options.errorField ?? field)}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}{options.required ? ' *' : ''}</label>
            <input {...fieldProps(k(options.errorField ?? field), e(options.errorField ?? field))} type={options.type ?? 'text'} value={terms[field]} maxLength={options.maxLength ?? 200} list={options.list} disabled={locked}
                onChange={(event) => onChange({ [field]: event.target.value } as Partial<RoleTermsDraft>)} className={`${inputClass}${options.mono ? ' font-mono uppercase' : ''}`} />
            <FieldError id={k(options.errorField ?? field)} messages={e(options.errorField ?? field)} />
        </div>
    );

    return (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-4" data-testid={`${prefix}-terms`}>
            <div className="sm:col-span-2" data-testid={`${prefix}-payment-terms`}>
                <div className="grid grid-cols-[minmax(0,1fr)_auto] items-end gap-x-3 gap-y-1 sm:grid-cols-[minmax(0,9rem)_auto_minmax(0,6rem)_minmax(0,7rem)]">
                    {numberInput('paymentTermsDays', 'relations_terms_days', { placeholder: t('relations_terms_days_placeholder', { days: String(defaultDays) }) })}
                    <label className="flex items-center gap-1 pb-1.5 text-sm text-gray-900">
                        <input type="checkbox" id={`${k('paymentTermsEndOfMonth')}-input`} checked={terms.paymentTermsEndOfMonth} disabled={locked}
                            onChange={(event) => onChange({ paymentTermsEndOfMonth: event.target.checked })} />
                        {t('relations_terms_end_of_month')}
                    </label>
                    {numberInput('discountPercent', 'relations_terms_discount_percent', { decimal: true })}
                    {numberInput('discountDays', 'relations_terms_discount_days')}
                </div>
                <span className="block text-xs text-gray-500">{t('relations_terms_discount_optional')}</span>
                {keepsInactive && <span className="block text-xs text-amber-800">{t('relations_terms_condition_inactive')}</span>}
                {preview !== null && (
                    <span className="block text-xs text-gray-600" data-testid={`${prefix}-terms-example`}>
                        {t('relations_terms_condition_example', { invoice: formatDateOnly(today, locale), due: formatDateOnly(preview.due, locale) })}
                        {preview.discountPercent !== null && preview.discountDate !== null && <> · {t('relations_terms_discount_example', { percent: preview.discountPercent.toLocaleString(locale === 'en' ? 'en-GB' : 'nl-NL'), date: formatDateOnly(preview.discountDate, locale) })}</>}
                    </span>
                )}
            </div>
            <div>
                {label('paymentMethod', 'relations_terms_payment_method')}
                <select {...fieldProps(k('paymentMethod'), e('paymentMethod'))} value={terms.paymentMethod || 'BankTransfer'} disabled={locked} className={inputClass}
                    onChange={(event) => onChange({ paymentMethod: event.target.value })}>
                    {reference.paymentMethods.map((method) => <option key={method} value={method}>{t(`relations_payment_method_${method}`)}</option>)}
                </select>
                <FieldError id={k('paymentMethod')} messages={e('paymentMethod')} />
            </div>
            {input('currency', prefix === 'customer' ? 'relations_field_currency_sales' : 'relations_field_currency', { maxLength: 3, mono: true, list: 'relation-currencies', errorField: currencyField })}
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
