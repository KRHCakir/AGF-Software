'use client';

import { type ReactNode, useEffect, useId, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from '../../i18n/LanguageContext';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

// Zijpaneel over een scherm heen (feedback 22 november: nieuw artikel vanuit een order). Geen navigatie: de pagina eronder blijft open
// en ongewijzigd, dus de vraag bij weggaan (fixes 5b) speelt hier niet. Het paneel staat in een portal buiten het formulier van de
// pagina, zodat typen in het paneel dat formulier niet "gewijzigd" maakt. Focus blijft in het paneel (Tab loopt rond), Escape vraagt
// het sluiten aan (onRequestClose beslist, bijvoorbeeld met een vraag), en na sluiten gaat de focus terug naar waar hij was.
// Breed scherm: rechts, maximaal 36rem; op 390 px vult het paneel het scherm.
export default function SidePanel({ title, onRequestClose, children, testId, dataTour }: {
    title: string;
    onRequestClose: () => void;
    children: ReactNode;
    testId?: string;
    dataTour?: string;
}) {
    const { t } = useTranslation();
    const titleId = useId();
    const panel = useRef<HTMLElement | null>(null);
    const close = useRef(onRequestClose);
    close.current = onRequestClose;

    useEffect(() => {
        const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        const first = panel.current?.querySelector<HTMLElement>('[data-autofocus]') ?? panel.current?.querySelector<HTMLElement>(FOCUSABLE);
        first?.focus();
        return () => { if (previous && document.contains(previous)) previous.focus(); };
    }, []);

    // React laat events uit een portal door de React-boom omhoog bubbelen: een submit in het paneel zou anders ook het formulier van
    // de pagina versturen (de order!), en invoer zou daar als wijziging tellen. Het paneel houdt daarom zijn events bij zich.
    const keep = (event: React.SyntheticEvent) => event.stopPropagation();

    const onKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
        // Een open keuzelijst in het paneel vangt Escape zelf af (preventDefault); alleen daarna vraagt Escape het sluiten aan.
        event.stopPropagation();
        if (event.key === 'Escape' && !event.defaultPrevented) {
            event.preventDefault();
            close.current();
            return;
        }
        if (event.key !== 'Tab' || panel.current === null) return;
        const focusable = Array.from(panel.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((element) => element.offsetParent !== null || element === document.activeElement);
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };

    if (typeof document === 'undefined') return null;
    return createPortal(
        <div className="fixed inset-0 z-50 flex justify-end bg-black/30" data-testid={testId ? `${testId}-backdrop` : undefined}
            onSubmit={keep} onReset={keep} onChange={keep} onInput={keep} onKeyUp={keep} onClick={keep} onMouseDown={keep} onFocus={keep} onBlur={keep}>
            <section ref={panel} role="dialog" aria-modal="true" aria-labelledby={titleId} onKeyDown={onKeyDown} data-testid={testId} data-tour={dataTour}
                className="flex h-full w-full flex-col overflow-y-auto bg-white shadow-xl sm:max-w-xl">
                <header className="sticky top-0 z-10 flex items-center justify-between gap-2 border-b border-gray-200 bg-white px-4 py-2">
                    <h2 id={titleId} className="text-lg font-semibold text-gray-900">{title}</h2>
                    <button type="button" onClick={() => close.current()} className="rounded px-2 py-1 text-sm text-gray-700 hover:bg-gray-100" aria-label={t('side_panel_close')}>✕</button>
                </header>
                <div className="flex-1 p-4">{children}</div>
            </section>
        </div>,
        document.body,
    );
}
