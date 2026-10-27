'use client';

import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { MAX_CUSTOMER_SUGGESTIONS, describeCustomer, searchCustomers } from '../../lib/customerSearch';
import type { CustomerOption } from '../../types/referenceData';
import LookupField, { LookupMatch } from '../shared/LookupField';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Klant kiezen in een nieuwe verkooporder, op het gedeelde LookupField: zoeken vanaf 1 teken op zoekcode, naam, plaats en
// btw-nummer (exacte zoekcode eerst, dan "begint met", dan "bevat"), hooguit 8 suggesties. Pijltjes bewegen; Tab en Enter
// nemen de gemarkeerde (standaard de bovenste) over en gaan naar het volgende veld; Escape sluit. Geen treffer: "Nieuwe klant"
// met de getypte tekst als naam (de bestaande klantopdracht; hier wordt niets opgeslagen).
export default function CustomerPicker({ id, customers, value, onChange, onNewCustomer, disabled = false, invalid = false, describedBy, newCustomerDisabled = false }: {
    id: string;
    customers: readonly CustomerOption[];
    value: string;
    onChange: (customer: CustomerOption | null) => void;
    onNewCustomer: (name: string) => void;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    newCustomerDisabled?: boolean;
}) {
    const { t } = useTranslation();
    const [query, setQuery] = useState('');
    // Een al gekozen (ook inactieve) klant blijft zichtbaar; zoeken toont alleen kiesbare klanten.
    const selected = useMemo(() => {
        const customer = customers.find((candidate) => String(candidate.id) === value);
        return customer ? { description: describeCustomer(customer) } : null;
    }, [customers, value]);
    const search = useCallback((text: string): LookupMatch<CustomerOption>[] => searchCustomers(customers, text).map((customer) => ({
        key: String(customer.id), item: customer, description: describeCustomer(customer),
        exact: customer.searchCode !== null && customer.searchCode.toLowerCase() === text.trim().toLowerCase(),
        badge: customer.searchCode,
    })), [customers]);
    const noMatch = selected === null && query.trim() !== '' && search(query).length === 0;

    return (
        <>
            <LookupField<CustomerOption>
                id={id}
                ariaLabel={t('sales_customer')}
                selected={selected}
                search={search}
                onSelect={(customer) => onChange(customer)}
                onClear={() => onChange(null)}
                onQueryChange={setQuery}
                disabled={disabled}
                invalid={invalid}
                describedBy={describedBy}
                maxSuggestions={MAX_CUSTOMER_SUGGESTIONS}
                takeHighlighted
                placeholderKey="sales_customer_search_placeholder"
                hintKey="sales_customer_search_hint"
                noneKey="sales_customer_none"
                moreKey="product_picker_more"
                changeKey="sales_customer_change"
            />
            {noMatch && (
                <p className="mt-1 flex flex-wrap items-center gap-2 text-xs text-gray-700" data-testid="customer-no-match">
                    {t('sales_customer_not_found')}
                    <button type="button" className={`${secondaryButtonClass} px-2 py-1 text-xs`} disabled={newCustomerDisabled}
                        onClick={() => onNewCustomer(query.trim())}>
                        + {t('sales_new_customer_named', { name: query.trim() })}
                    </button>
                </p>
            )}
        </>
    );
}
