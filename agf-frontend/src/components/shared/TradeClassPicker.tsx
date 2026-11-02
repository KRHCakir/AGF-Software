'use client';

import { useCallback, useMemo } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { TradeClass, isTradeClass, searchTradeClasses } from '../../lib/tradeClass';
import LookupField, { LookupMatch } from './LookupField';

// Klasse kiezen uit de vaste lijst (Extra, I, II), zoals de landkiezer: typen met suggesties ("1", "klasse 2", "ex"), Tab en Enter nemen
// de bovenste. Er staat daarna altijd de vaste klasse; een schrijfwijze die niet herkend wordt, geeft geen suggestie en wordt nooit bewaard.
export default function TradeClassPicker({ id, value, onChange, disabled = false, invalid = false, describedBy, dataTour, dataTourKey, ariaLabel }: {
    id: string;
    // De gekozen klasse, of '' / null als er (nog) geen is.
    value: string | null;
    onChange: (tradeClass: TradeClass | '') => void;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    dataTour?: string;
    dataTourKey?: string | number;
    ariaLabel?: string;
}) {
    const { t } = useTranslation();
    const selected = useMemo(() => (isTradeClass(value) ? { description: value } : null), [value]);
    const search = useCallback((query: string): LookupMatch<TradeClass>[] => searchTradeClasses(query).map((match) => ({
        key: match.tradeClass,
        item: match.tradeClass,
        description: match.tradeClass,
        exact: match.exact,
    })), []);

    return (
        <LookupField<TradeClass>
            id={id}
            dataTour={dataTour}
            dataTourKey={dataTourKey}
            ariaLabel={ariaLabel ?? t('trade_class_label')}
            selected={selected}
            search={search}
            onSelect={(tradeClass) => onChange(tradeClass)}
            onClear={() => onChange('')}
            disabled={disabled}
            invalid={invalid}
            describedBy={describedBy}
            maxSuggestions={3}
            takeHighlighted
            placeholderKey="trade_class_placeholder"
            hintKey="trade_class_hint"
            noneKey="trade_class_none"
            moreKey="product_picker_more"
            changeKey="trade_class_change"
        />
    );
}
