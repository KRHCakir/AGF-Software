'use client';

import { KeyboardEvent, useEffect, useId, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateOnly } from '../../lib/calendarDate';
import type { CertificateCheck } from '../../types/certificate';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// "Klopt dit?" bij inkoop (certificaten deel 1): het product staat niet in het GGN-certificaat van de leverancier ("GGN … van … geldt voor
// mango en limoen, niet voor avocado"), of het certificaat is verlopen of verloopt vóór de levering. Een waarschuwing, geen blokkade: Ja
// legt vast wie bevestigde; Nee gaat terug naar het formulier. Nee heeft de focus en Escape is Nee, zodat Enter nooit per ongeluk Ja is.
// Modaal: Tab blijft tussen de twee knoppen (focusval, zoals bij de vraag over niet-opgeslagen wijzigingen); komt de focus toch buiten het
// venster, dan gaat die terug naar Nee; bij sluiten gaat de focus terug naar waar de gebruiker stond.
export function checkLines(check: CertificateCheck, t: (key: string, params?: Record<string, string>) => string, locale: string): string[] {
    const lower = (text: string) => (locale.startsWith('nl') ? text.toLowerCase() : text);
    const join = (items: string[]) => (items.length <= 1 ? items.join('') : `${items.slice(0, -1).join(', ')} ${t('cert_check_and')} ${items[items.length - 1]}`);
    const lines: string[] = [];
    if (check.kinds.includes('NotCovered')) {
        const covered = [...new Set(check.certificates.flatMap((c) => c.productNames))].map(lower);
        lines.push(t('cert_check_not_covered', { ggns: check.certificates.map((c) => c.number).join(', '), supplier: check.supplierName, covered: join(covered), product: lower(check.productName) }));
    }
    const last = check.certificates.map((c) => c.validUntil).filter((d): d is string => d !== null).sort().pop();
    if (check.kinds.includes('Expired') && last) lines.push(t('cert_check_expired', { date: formatDateOnly(last, locale) }));
    if (check.kinds.includes('ExpiresBeforeDelivery') && last) {
        lines.push(t('cert_check_expires_before', { date: formatDateOnly(last, locale), delivery: formatDateOnly(check.deliveryDate, locale) }));
    }
    return lines;
}

export default function CertificateCheckDialog({ checks, onNo, onYes }: { checks: CertificateCheck[]; onNo: () => void; onYes: () => void }) {
    const { t, locale } = useTranslation();
    const titleId = useId();
    const noRef = useRef<HTMLButtonElement | null>(null);
    const dialogRef = useRef<HTMLDivElement | null>(null);
    useEffect(() => {
        const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        noRef.current?.focus();
        const onOutside = (event: FocusEvent) => {
            if (event.target instanceof Node && dialogRef.current?.contains(event.target) !== true) noRef.current?.focus();
        };
        document.addEventListener('focusin', onOutside);
        return () => {
            document.removeEventListener('focusin', onOutside);
            if (previous?.isConnected) setTimeout(() => previous.focus(), 0);
        };
    }, []);
    const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.key === 'Escape') { event.preventDefault(); onNo(); return; }
        if (event.key !== 'Tab') return;
        const buttons = Array.from(dialogRef.current?.querySelectorAll<HTMLButtonElement>('button') ?? []);
        if (buttons.length === 0) return;
        event.preventDefault();
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
        buttons[(index + (event.shiftKey ? buttons.length - 1 : 1)) % buttons.length].focus();
    };
    return createPortal(
        <div className="fixed inset-0 z-50 flex items-end justify-center bg-gray-900/40 p-0 sm:items-center sm:p-4" onKeyDown={onKeyDown}>
            <div ref={dialogRef} role="alertdialog" aria-modal="true" aria-labelledby={titleId} data-tour="certificate-check-dialog" data-testid="certificate-check-dialog"
                className="max-h-full w-full overflow-y-auto rounded-t-lg bg-white p-4 pb-20 shadow-xl sm:max-w-xl sm:rounded-lg sm:pb-4">
                <h2 id={titleId} className="text-lg font-semibold text-gray-900">{t('cert_check_title')}</h2>
                <ul className="mt-2 space-y-2 text-sm text-gray-900">
                    {checks.map((check) => (
                        <li key={check.productId} className="rounded border border-amber-300 bg-amber-50 px-3 py-2" data-testid={`certificate-check-${check.productId}`}>
                            {checkLines(check, t, locale).map((line) => <p key={line}>{line}</p>)}
                        </li>
                    ))}
                </ul>
                <p className="mt-2 text-xs text-gray-700">{t('cert_check_audit')}</p>
                <div className="mt-4 flex flex-col gap-2 sm:flex-row sm:justify-end">
                    <button ref={noRef} type="button" className={secondaryButtonClass} onClick={onNo} data-tour="certificate-check-no">{t('cert_check_no')}</button>
                    <button type="button" className={primaryButtonClass} onClick={onYes} data-tour="certificate-check-yes">{t('cert_check_yes')}</button>
                </div>
            </div>
        </div>,
        document.body,
    );
}
