'use client';

import { useCallback, useMemo } from 'react';
import { SearchableVendor, describeVendor, searchVendors } from '../../lib/purchaseInvoiceRules';
import LookupField, { LookupMatch } from '../shared/LookupField';

// Crediteur/leverancier zoeken op naam, zoekcode, btw-nummer en #nummer, op het gedeelde LookupField: typen in plaats van
// een lange lijst doorlopen. Gebruikt in de inkoopfactuur, Aanvoer plannen en het filter van Inkoopopdrachten.
export default function VendorPicker<T extends SearchableVendor & { isActive: boolean }>({ id, vendors, value, onChange, disabled = false, invalid = false, describedBy, ariaLabel, inputRef, badge, includeInactive = false, dataTour }: {
    id: string;
    vendors: readonly T[];
    value: string;
    onChange: (vendor: T | null) => void;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    ariaLabel: string;
    inputRef?: (element: HTMLInputElement | null) => void;
    // Extra kenmerk rechts in de suggestie (de inkoopfactuur toont de betaaltermijn).
    badge?: (vendor: T) => string | null;
    // Filteren op bestaande orders: ook inactieve leveranciers zijn dan vindbaar.
    includeInactive?: boolean;
    // Vaste naam voor de rondleiding (data-tour) op het zoekveld.
    dataTour?: string;
}) {
    const selected = useMemo(() => {
        const vendor = vendors.find((candidate) => String(candidate.id) === value);
        return vendor ? { description: describeVendor(vendor) } : null;
    }, [vendors, value]);
    const search = useCallback((query: string): LookupMatch<T>[] => {
        const term = query.trim();
        const exactNumber = /^#?(\d{1,10})$/.exec(term);
        // Inactieve relaties zijn niet kiesbaar voor een nieuwe factuur (de server weigert ze ook); een al gekozen waarde blijft zichtbaar.
        return searchVendors(vendors.filter((vendor) => includeInactive || vendor.isActive), query).map((vendor) => ({
            key: String(vendor.id), item: vendor, description: describeVendor(vendor),
            exact: (exactNumber !== null && vendor.id === Number(exactNumber[1])) || (vendor.searchCode !== null && vendor.searchCode.toLowerCase() === term.toLowerCase()),
            badge: badge ? badge(vendor) : null,
        }));
    }, [vendors, badge, includeInactive]);

    return (
        <LookupField<T>
            id={id}
            dataTour={dataTour}
            ariaLabel={ariaLabel}
            selected={selected}
            search={search}
            onSelect={(vendor) => onChange(vendor)}
            onClear={() => onChange(null)}
            disabled={disabled}
            invalid={invalid}
            describedBy={describedBy}
            inputRef={inputRef}
            placeholderKey="purchase_vendor_placeholder"
            hintKey="product_picker_hint"
            noneKey="purchase_vendor_none"
            moreKey="product_picker_more"
            changeKey="purchase_vendor_change"
        />
    );
}
