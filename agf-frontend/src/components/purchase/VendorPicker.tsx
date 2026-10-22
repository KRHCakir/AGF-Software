'use client';

import { useCallback, useMemo } from 'react';
import { describeVendor, searchVendors } from '../../lib/purchaseInvoiceRules';
import type { PurchaseVendor } from '../../types/purchaseInvoice';
import LookupField, { LookupMatch } from '../shared/LookupField';

// Crediteur/leverancier zoeken op naam, btw-nummer en #nummer, op het gedeelde LookupField.
export default function VendorPicker({ id, vendors, value, onChange, disabled = false, invalid = false, describedBy, ariaLabel, inputRef }: {
    id: string;
    vendors: readonly PurchaseVendor[];
    value: string;
    onChange: (vendor: PurchaseVendor | null) => void;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    ariaLabel: string;
    inputRef?: (element: HTMLInputElement | null) => void;
}) {
    const selected = useMemo(() => {
        const vendor = vendors.find((candidate) => String(candidate.id) === value);
        return vendor ? { description: describeVendor(vendor) } : null;
    }, [vendors, value]);
    const search = useCallback((query: string): LookupMatch<PurchaseVendor>[] => {
        const term = query.trim();
        const exactNumber = /^#?(\d{1,10})$/.exec(term);
        // Inactieve relaties zijn niet kiesbaar voor een nieuwe factuur (de server weigert ze ook); een al gekozen waarde blijft zichtbaar.
        return searchVendors(vendors.filter((vendor) => vendor.isActive), query).map((vendor) => ({
            key: String(vendor.id), item: vendor, description: describeVendor(vendor),
            exact: (exactNumber !== null && vendor.id === Number(exactNumber[1])) || (vendor.searchCode !== null && vendor.searchCode.toLowerCase() === term.toLowerCase()),
            badge: vendor.paymentTermsDays > 0 ? `${vendor.paymentTermsDays} d` : null,
        }));
    }, [vendors]);

    return (
        <LookupField<PurchaseVendor>
            id={id}
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
