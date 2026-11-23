'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import type { DeliveryTermDraft } from '../../lib/deliveryTerm';
import { DELIVERY_TERM_KINDS, INCOTERMS_2020 } from '../../lib/roleTermsRules';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';

// Leveringsconditie van een order (feedback 22 november): soort, en bij Incoterm de code en de plaats, anders een toelichting. Dezelfde
// keuzes als bij de relatie; de waarde hoort bij de order, de relatie verandert niet. `hint` zegt waar de voorinvulling vandaan komt.
export default function DeliveryTermField({ value, onChange, errors, locked = false, hint = null, dataTour = 'delivery-term' }: {
    value: DeliveryTermDraft;
    onChange: (next: DeliveryTermDraft) => void;
    errors: (key: string) => string[] | undefined;
    locked?: boolean;
    hint?: string | null;
    dataTour?: string;
}) {
    const { t } = useTranslation();
    const key = (field: string) => `deliveryTerm.${field}`;
    return (
        <fieldset className="grid grid-cols-2 gap-2 sm:grid-cols-4" data-tour={dataTour}>
            <legend className="sr-only">{t('relations_terms_delivery')}</legend>
            <div className="col-span-2 sm:col-span-1">
                <label htmlFor={`${key('deliveryTermKind')}-input`} className="text-xs font-medium text-gray-700">{t('relations_terms_delivery')}</label>
                <select {...fieldProps(key('deliveryTermKind'), errors(key('deliveryTermKind')))} value={value.kind} disabled={locked} className={inputClass}
                    onChange={(event) => onChange({ ...value, kind: event.target.value })}>
                    <option value="">{t('delivery_term_none')}</option>
                    {DELIVERY_TERM_KINDS.map((kind) => <option key={kind} value={kind}>{t(`relations_delivery_${kind}`)}</option>)}
                </select>
                <FieldError id={key('deliveryTermKind')} messages={errors(key('deliveryTermKind'))} />
            </div>
            {value.kind === 'Incoterm' && (
                <>
                    <div>
                        <label htmlFor={`${key('incoterm')}-input`} className="text-xs font-medium text-gray-700">{t('relations_terms_incoterm')} *</label>
                        <select {...fieldProps(key('incoterm'), errors(key('incoterm')))} value={value.incoterm.toUpperCase()} disabled={locked} className={inputClass}
                            onChange={(event) => onChange({ ...value, incoterm: event.target.value })}>
                            <option value="">–</option>
                            {INCOTERMS_2020.map((code) => <option key={code} value={code}>{code}</option>)}
                        </select>
                        <FieldError id={key('incoterm')} messages={errors(key('incoterm'))} />
                    </div>
                    <div className="col-span-2 sm:col-span-1">
                        <label htmlFor={`${key('incotermPlace')}-input`} className="text-xs font-medium text-gray-700">{t('relations_terms_incoterm_place')} *</label>
                        <input {...fieldProps(key('incotermPlace'), errors(key('incotermPlace')))} value={value.place} disabled={locked} maxLength={100} className={inputClass}
                            onChange={(event) => onChange({ ...value, place: event.target.value })} />
                        <FieldError id={key('incotermPlace')} messages={errors(key('incotermPlace'))} />
                    </div>
                </>
            )}
            {value.kind !== '' && value.kind !== 'Incoterm' && (
                <div className="col-span-2">
                    <label htmlFor={`${key('deliveryTermText')}-input`} className="text-xs font-medium text-gray-700">
                        {t(value.kind === 'Other' ? 'relations_terms_delivery_text_required' : 'relations_terms_delivery_text')}{value.kind === 'Other' ? ' *' : ''}
                    </label>
                    <input {...fieldProps(key('deliveryTermText'), errors(key('deliveryTermText')))} value={value.text} disabled={locked} maxLength={200} className={inputClass}
                        onChange={(event) => onChange({ ...value, text: event.target.value })} />
                    <FieldError id={key('deliveryTermText')} messages={errors(key('deliveryTermText'))} />
                </div>
            )}
            {hint && <p className="col-span-2 text-xs text-gray-600 sm:col-span-4" data-testid="delivery-term-hint">{hint}</p>}
        </fieldset>
    );
}
