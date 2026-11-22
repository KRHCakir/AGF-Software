'use client';

import { useCallback, useMemo } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { MAX_COUNTRY_SUGGESTIONS, countryLabel, searchCountries } from '../../lib/countries';
import type { CountryReference } from '../../types/country';
import LookupField, { LookupMatch } from './LookupField';

// Herkomst kiezen uit de vaste landenlijst: typen met suggesties op code ("PE"), Nederlandse en Engelse naam en eigen aliassen;
// Tab en Enter nemen de bovenste. Er staat daarna de code met de naam in de taal van het scherm ("PE Peru"); alleen de code gaat naar de server.
export default function CountryPicker({ id, countries, value, onChange, disabled = false, invalid = false, describedBy, dataTour, dataTourKey, ariaLabel }: {
    id: string;
    countries: CountryReference;
    value: string | null;
    onChange: (code: string | null) => void;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    dataTour?: string;
    dataTourKey?: string | number;
    ariaLabel?: string;
}) {
    const { t, locale } = useTranslation();
    const selected = useMemo(() => (value === null ? null : { description: countryLabel(countries, value, locale) }), [countries, value, locale]);
    const search = useCallback((query: string): LookupMatch<string>[] => searchCountries(countries, query).map((match) => ({
        key: match.country.code,
        item: match.country.code,
        description: locale === 'nl' ? match.country.nameNl : match.country.nameEn,
        exact: match.exact,
        badge: match.alias === null ? match.country.code : `${match.alias} → ${match.country.code}`,
    })), [countries, locale]);

    return (
        <LookupField<string>
            id={id}
            dataTour={dataTour}
            dataTourKey={dataTourKey}
            ariaLabel={ariaLabel ?? t('country_label')}
            selected={selected}
            search={search}
            onSelect={(code) => onChange(code)}
            onClear={() => onChange(null)}
            disabled={disabled}
            invalid={invalid}
            describedBy={describedBy}
            maxSuggestions={MAX_COUNTRY_SUGGESTIONS}
            takeHighlighted
            placeholderKey="country_search_placeholder"
            hintKey="country_search_hint"
            noneKey="country_none"
            moreKey="product_picker_more"
            changeKey="country_change"
        />
    );
}
