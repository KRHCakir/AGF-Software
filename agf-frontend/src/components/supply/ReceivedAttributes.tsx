'use client';

import { ReactNode, useCallback, useMemo } from 'react';
import Link from 'next/link';
import { useTranslation } from '../../i18n/LanguageContext';
import { EnteredAttributes, RECEIPT_ATTRIBUTES, ReceiptAttribute, optionsFor } from '../../lib/receiptBlindEntry';
import type { CountryReference } from '../../types/country';
import type { VariantOption } from '../../types/referenceData';
import CountryPicker from '../shared/CountryPicker';
import LookupField, { LookupMatch } from '../shared/LookupField';
import FieldError from './FieldError';

const LABEL_KEYS: Record<ReceiptAttribute, string> = {
    size: 'receipt_attr_size', packaging: 'receipt_attr_packaging', tradeClass: 'receipt_attr_trade_class', brand: 'receipt_attr_brand',
};
const TOUR_NAMES: Record<ReceiptAttribute, string> = {
    size: 'receive-size', packaging: 'receive-packaging', tradeClass: 'receive-class', brand: 'receive-brand',
};

// Wat er werkelijk is aangekomen, per kenmerk en zonder voorinvulling: herkomst (verplicht; landkiezer met snelkoppelingen) en
// optioneel maat, verpakking, klasse en merk (leeg = bewust "geen"). Elk veld typt met suggesties uit de bestaande varianten van dit
// artikel, gefilterd op wat al is ingevuld; Tab neemt de bovenste. Een combinatie die niet bestaat: de melding "Nieuw artikel" met
// "Artikel aanmaken en doorgaan" (missingPrompt); hier zelf wordt nooit iets aangemaakt.
export default function ReceivedAttributes({ lineId, variants, countries, value, onChange, disabled, errors, combination, missingPrompt }: {
    lineId: number;
    variants: readonly VariantOption[];
    countries: CountryReference;
    value: EnteredAttributes;
    onChange: (patch: Partial<EnteredAttributes>) => void;
    disabled: boolean;
    errors: (field: string) => string[] | undefined;
    // 'missing' of 'ambiguous' na registreren: deze invoer past niet op precies één artikel.
    combination: 'missing' | 'ambiguous' | null;
    missingPrompt?: ReactNode;
}) {
    const { t } = useTranslation();
    const names = useMemo(() => new Map(countries.countries.map((country) => [country.code, country.nameNl])), [countries]);
    const key = (field: string) => `line.${lineId}.${field}`;

    return (
        <div className="col-span-2 grid grid-cols-2 gap-3 md:col-span-7 md:grid-cols-5" data-tour="receive-attributes" data-tour-key={lineId}>
            <div className="col-span-2 md:col-span-1">
                <label htmlFor={`${key('origin')}-input`} className="text-xs font-medium text-gray-700">{t('receipt_attr_origin')} *</label>
                <CountryPicker id={key('origin')} dataTour="receive-origin" dataTourKey={lineId} countries={countries} value={value.origin}
                    onChange={(origin) => onChange({ origin })} disabled={disabled} invalid={errors(key('origin')) !== undefined} />
                <FieldError id={key('origin')} messages={errors(key('origin'))} />
            </div>
            {RECEIPT_ATTRIBUTES.map((attribute) => (
                <AttributeField key={attribute} id={key(attribute)} label={`${t(LABEL_KEYS[attribute])} ${t('field_optional')}`} dataTour={TOUR_NAMES[attribute]} lineId={lineId}
                    options={optionsFor(variants, value, attribute, names)} value={value[attribute]} disabled={disabled}
                    invalid={errors(key(attribute)) !== undefined} onChange={(text) => onChange({ [attribute]: text } as Partial<EnteredAttributes>)}
                    messages={errors(key(attribute))} />
            ))}
            {combination === 'missing' && missingPrompt !== undefined && <div className="col-span-2 md:col-span-5">{missingPrompt}</div>}
            {combination !== null && (combination === 'ambiguous' || missingPrompt === undefined) && (
                <p role="alert" className="col-span-2 rounded border border-red-300 bg-red-50 px-2 py-1 text-xs font-medium text-red-900 md:col-span-5"
                    data-tour="receive-combination-missing" data-tour-key={lineId}>
                    {t(combination === 'missing' ? 'receipt_combination_missing' : 'receipt_combination_ambiguous')}{' '}
                    <Link href="/articles" className="underline">{t('receipt_combination_to_articles')}</Link>
                </p>
            )}
        </div>
    );
}

// Eén kenmerk: getypte tekst blijft staan (vrij), suggesties uit de bestaande varianten; Tab neemt de bovenste.
function AttributeField({ id, label, dataTour, lineId, options, value, disabled, invalid, onChange, messages }: {
    id: string;
    label: string;
    dataTour: string;
    lineId: number;
    options: readonly string[];
    value: string;
    disabled: boolean;
    invalid: boolean;
    onChange: (value: string) => void;
    messages: string[] | undefined;
}) {
    const search = useCallback((query: string): LookupMatch<string>[] => {
        const term = query.trim().toLowerCase();
        return options
            .filter((option) => term === '' || option.toLowerCase().includes(term))
            .sort((a, b) => Number(b.toLowerCase().startsWith(term)) - Number(a.toLowerCase().startsWith(term)))
            .map((option) => ({ key: option, item: option, description: option, exact: option.toLowerCase() === term }));
    }, [options]);
    return (
        <div>
            <label htmlFor={`${id}-input`} className="text-xs font-medium text-gray-700">{label}</label>
            <LookupField<string>
                id={id}
                dataTour={dataTour}
                dataTourKey={lineId}
                ariaLabel={label}
                selected={value === '' ? null : { description: value }}
                search={search}
                onSelect={onChange}
                onClear={() => onChange('')}
                // Bij het verlaten na een keuze is de zoektekst leeg: dat is geen nieuwe waarde (wissen gaat met ✕ of Backspace).
                onFreeText={(text) => { if (text.trim() !== '') onChange(text.trim()); }}
                freeText={value}
                allowFreeText
                disabled={disabled}
                invalid={invalid}
                takeHighlighted
                placeholderKey="receipt_attr_placeholder"
                hintKey="receipt_attr_hint"
                noneKey="receipt_attr_none"
                moreKey="product_picker_more"
                changeKey="receipt_attr_change"
            />
            <FieldError id={id} messages={messages} />
        </div>
    );
}
