'use client';

import { useId, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg } from '../../lib/availability';
import type { VariantOption } from '../../types/referenceData';
import { secondaryButtonClass } from './SuccessPanel';

// "Toon verwachte kenmerken" per orderregel bij blinde invoer (standaard dicht): de herkomst, maat, verpakking, klasse, merk en kg per
// colli van de bestelling, voor wie twijfelt. De eerste keer openen legt het moment vast (onShow); dat gaat mee met de ontvangst, zodat
// later zichtbaar is dat de verwachting vooraf bekend was. De controle na "Pallet registreren" blijft altijd gelden.
export default function ExpectedAttributes({ lineId, variant, kgPerPackage, shownAt, onShow, disabled }: {
    lineId: number;
    variant: VariantOption | undefined;
    kgPerPackage: number | null;
    shownAt: string | null;
    onShow: () => void;
    disabled: boolean;
}) {
    const { t, locale } = useTranslation();
    const [open, setOpen] = useState(false);
    const panelId = useId();
    const none = t('articles_value_none');
    const value = (text: string | null | undefined) => (text === null || text === undefined || text.trim() === '' ? none : text.trim());

    const toggle = () => {
        if (!open && shownAt === null) onShow();
        setOpen(!open);
    };

    return (
        <div className="mb-2 text-xs">
            <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} aria-expanded={open} aria-controls={panelId} disabled={disabled && shownAt === null}
                onClick={toggle} data-tour="receive-show-expected" data-tour-key={lineId}>
                {t(open ? 'receive_expected_hide' : 'receive_expected_show')}
            </button>
            {shownAt !== null && !open && <span className="ml-2 text-gray-600" data-testid={`expected-shown-${lineId}`}>{t('receive_expected_recorded', { when: formatDateTime(shownAt, locale) })}</span>}
            {open && (
                <div id={panelId} className="mt-1 rounded border border-gray-200 bg-gray-50 p-2" data-testid={`expected-attributes-${lineId}`}>
                    <dl className="grid grid-cols-2 gap-x-3 gap-y-0.5 sm:grid-cols-3">
                        <div><dt className="inline text-gray-600">{t('receipt_attr_origin')}: </dt><dd className="inline text-gray-900">{value(variant?.origin)}</dd></div>
                        <div><dt className="inline text-gray-600">{t('receipt_attr_size')}: </dt><dd className="inline text-gray-900">{value(variant?.sizeCategory)}</dd></div>
                        <div><dt className="inline text-gray-600">{t('receipt_attr_packaging')}: </dt><dd className="inline text-gray-900">{value(variant?.packaging)}</dd></div>
                        <div><dt className="inline text-gray-600">{t('receipt_attr_trade_class')}: </dt><dd className="inline text-gray-900">{value(variant?.tradeClass)}</dd></div>
                        <div><dt className="inline text-gray-600">{t('receipt_attr_brand')}: </dt><dd className="inline text-gray-900">{value(variant?.brand)}</dd></div>
                        <div><dt className="inline text-gray-600">{t('receive_expected_kg')}: </dt><dd className="inline text-gray-900">{kgPerPackage === null ? none : formatKg(kgPerPackage, locale)}</dd></div>
                    </dl>
                    <p className="mt-1 text-gray-600">{t('receive_expected_note')}</p>
                </div>
            )}
        </div>
    );
}
