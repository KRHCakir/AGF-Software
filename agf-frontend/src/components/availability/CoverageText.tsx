'use client';

import Link from 'next/link';
import { Fragment } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { CoveragePart, dayMonth } from '../../lib/salesSupply';

// Eén regel tekst bij een orderregel: hoeveel gereserveerd is en hoe de rest gedekt is (oudste verkoop eerst): nu uit voorraad te
// reserveren, in keuring of verwacht per inkooporder (met link), en het tekort in rood. Niets als de regel helemaal gereserveerd is.
export default function CoverageText({ parts, reserved = 0, testId }: { parts: CoveragePart[]; reserved?: number; testId?: string }) {
    const { t, locale } = useTranslation();
    const q = (value: number) => formatQuantity(value, locale);
    const items: React.ReactNode[] = [];
    if (reserved > 0) items.push(<span key="reserved">{t('sales_cover_reserved', { quantity: q(reserved) })}</span>);
    // Sleutel met de plaats: dezelfde inkooporder kan meer dan eens voorkomen (bijvoorbeeld deels in keuring en deels verwacht).
    parts.forEach((part, index) => {
        const order = (number: string | null, id: number) => (
            <Link href={`/purchase-orders?id=${id}`} className="font-medium text-blue-800 underline underline-offset-2">{number ?? `#${id}`}</Link>
        );
        if (part.kind === 'stock') items.push(<span key={`s${index}`} className="text-emerald-800">{t('sales_cover_stock', { quantity: q(part.quantity) })}</span>);
        if (part.kind === 'inspection') items.push(<span key={`i${index}`}>{t('sales_cover_inspection', { quantity: q(part.quantity) })} {order(part.orderNumber, part.supplyCommitmentId)}</span>);
        if (part.kind === 'expected') items.push(
            <span key={`e${index}`}>
                {t('sales_cover_expected', { quantity: q(part.quantity) })} {order(part.orderNumber, part.supplyCommitmentId)}
                {part.expectedAt && <> ({t('sales_cover_expected_on', { date: dayMonth(part.expectedAt) })})</>}
            </span>,
        );
        if (part.kind === 'shortage') items.push(<span key={`t${index}`} className="font-semibold text-red-800">{t('sales_cover_shortage', { quantity: q(part.quantity) })}</span>);
    });
    if (items.length === 0) return null;
    return (
        <p className="text-xs text-gray-700" data-testid={testId}>
            {items.map((item, index) => <Fragment key={index}>{index > 0 && ' · '}{item}</Fragment>)}
        </p>
    );
}
