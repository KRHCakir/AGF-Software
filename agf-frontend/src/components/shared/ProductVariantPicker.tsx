'use client';

import { useCallback, useMemo } from 'react';
import { findVariant, searchVariants } from '../../lib/productSearch';
import type { SearchableProduct, VariantMatch } from '../../lib/productSearch';
import LookupField, { LookupMatch } from './LookupField';

// Gedeeld doorzoekbaar productveld (Aanvoer plannen, Nieuwe verkooporder, Inkoopfactuur) op het gedeelde LookupField.
// Zoekt op product, variant, verpakking, herkomst, maatklasse en de bestaande snelcode (ShortcutCode). Een snelcode
// kiest uitsluitend een variant; hoeveelheid, prijs of gewicht wordt hier nooit ingevuld.
// Met `allowFreeText` (inkoopfactuur) blijft een getypte tekst zonder keuze staan als vrije omschrijving.

export default function ProductVariantPicker({ id, products, value, onChange, disabled = false, invalid = false, describedBy, ariaLabel, allowFreeText = false, freeText = '', onFreeText, inputRef, className }: {
    id: string;
    products: readonly SearchableProduct[];
    value: string;
    onChange: (variantId: string) => void;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    ariaLabel: string;
    allowFreeText?: boolean;
    freeText?: string;
    onFreeText?: (text: string) => void;
    inputRef?: (element: HTMLInputElement | null) => void;
    className?: string;
}) {
    const selected = useMemo(() => (value === '' ? null : findVariant(products, Number(value))), [products, value]);
    const search = useCallback((query: string): LookupMatch<VariantMatch>[] => searchVariants(products, query).map((match) => ({
        key: String(match.variantId),
        item: match,
        description: match.description,
        exact: match.exactShortcut,
        badge: match.variant.shortcutCode,
        badgeStrong: match.exactShortcut,
    })), [products]);

    return (
        <LookupField<VariantMatch>
            id={id}
            ariaLabel={ariaLabel}
            selected={selected}
            search={search}
            onSelect={(match) => onChange(String(match.variantId))}
            onClear={() => onChange('')}
            allowFreeText={allowFreeText}
            freeText={freeText}
            onFreeText={onFreeText}
            disabled={disabled}
            invalid={invalid}
            describedBy={describedBy}
            inputRef={inputRef}
            className={className}
            placeholderKey={allowFreeText ? 'purchase_line_item_placeholder' : 'product_picker_placeholder'}
            hintKey="product_picker_hint"
            noneKey={allowFreeText ? 'purchase_line_item_none' : 'product_picker_none'}
            moreKey="product_picker_more"
            changeKey="product_picker_change"
        />
    );
}
