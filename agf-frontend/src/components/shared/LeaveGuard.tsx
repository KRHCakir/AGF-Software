'use client';

import { usePathname, useRouter } from 'next/navigation';
import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Fixes 5b, een fout blokkeert het weggaan. Een formulier meldt zich met useLeaveGuard(gewijzigd, fout). Zolang een formulier een
// invoerfout heeft of niet-opgeslagen wijzigingen, kan de gebruiker niet naar een andere pagina:
//   - een link (menu, submenu, keten-strip, knoppen die links zijn) vraagt eerst "Je formulier bevat nog een fout / niet-opgeslagen
//     wijzigingen." met Terug naar het formulier (standaard) en Annuleren en weggooien;
//   - de browserknop Terug: een extra geschiedenisregel op dezelfde pagina vangt hem op en stelt dezelfde vraag;
//   - het tabblad sluiten of herladen: de eigen waarschuwing van de browser (beforeunload).
// Een onaangeroerd formulier blokkeert niets. Alleen weggaan van de pagina telt; binnen de pagina werken knoppen gewoon.

type GuardState = 'none' | 'dirty' | 'error';
type Pending = { kind: 'href'; href: string } | { kind: 'back' };

interface LeaveGuardApi {
    set: (id: string, dirty: boolean, error: boolean) => void;
    remove: (id: string) => void;
}

const LeaveGuardContext = createContext<LeaveGuardApi | null>(null);

// Het formulier geeft door of er iets gewijzigd is en of er een invoerfout staat. Buiten de provider (losse tests) doet het niets.
export function useLeaveGuard(dirty: boolean, error: boolean): void {
    const api = useContext(LeaveGuardContext);
    const id = useId();
    useEffect(() => { api?.set(id, dirty, error); }, [api, id, dirty, error]);
    useEffect(() => () => api?.remove(id), [api, id]);
}

// Voor een formulier: `ref` op het formulier-element. Elke invoer van de gebruiker in dat element (input/change: typen, kiezen, plakken,
// ook de rondleiding) maakt het formulier "gewijzigd"; `saved()` na geslaagd opslaan maakt het weer schoon. Zo telt alleen wat de
// gebruiker deed, nooit gegevens die het formulier zelf laadt. `error` is een invoerfout die nu in beeld staat.
export function useFormLeaveGuard(error: boolean): { ref: (element: HTMLElement | null) => void; saved: () => void; touched: boolean } {
    const [element, setElement] = useState<HTMLElement | null>(null);
    const [touched, setTouched] = useState(false);
    useEffect(() => {
        if (element === null) return;
        // Op window en niet op het formulier zelf: dan komt deze luisteraar ná de onChange van React (die op de root luistert). Op het
        // formulier zou setTouched eerder komen, React tekende het veld dan opnieuw met de oude waarde en de eerste toets ging verloren.
        const onInput = (event: Event) => { if (event.target instanceof Node && element.contains(event.target)) setTouched(true); };
        window.addEventListener('input', onInput);
        window.addEventListener('change', onInput);
        return () => { window.removeEventListener('input', onInput); window.removeEventListener('change', onInput); };
    }, [element]);
    useLeaveGuard(touched, error);
    const saved = useCallback(() => setTouched(false), []);
    return { ref: setElement, saved, touched };
}

export function guardStateOf(entries: Iterable<{ dirty: boolean; error: boolean }>): GuardState {
    let state: GuardState = 'none';
    for (const entry of entries) {
        if (entry.error) return 'error';
        if (entry.dirty) state = 'dirty';
    }
    return state;
}

// Een klik op een link naar een andere pagina van de software (zelfde website, ander pad of andere zoekvraag). Nieuwe tabbladen,
// downloads en klikken met een modificatietoets laten we met rust: daarbij blijft het formulier staan.
export function leavingHref(event: Pick<MouseEvent, 'button' | 'metaKey' | 'ctrlKey' | 'shiftKey' | 'altKey' | 'defaultPrevented' | 'target'>, location: { href: string; origin: string; pathname: string; search: string }): string | null {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return null;
    const target = event.target as { closest?: (selector: string) => Element | null } | null;
    const anchor = typeof target?.closest === 'function' ? target.closest('a[href]') as HTMLAnchorElement | null : null;
    if (anchor === null || (anchor.target !== '' && anchor.target !== '_self') || anchor.hasAttribute('download')) return null;
    const url = new URL(anchor.getAttribute('href') ?? '', location.href);
    if (url.origin !== location.origin) return null;
    if (url.pathname === location.pathname && url.search === location.search) return null;
    return url.pathname + url.search + url.hash;
}

export default function LeaveGuardProvider({ children }: { children: ReactNode }) {
    const { t } = useTranslation();
    const router = useRouter();
    const pathname = usePathname();
    const entries = useRef(new Map<string, { dirty: boolean; error: boolean }>());
    const [computed, setComputed] = useState<GuardState>('none');
    // Na "Annuleren en weggooien": tot de volgende pagina is het formulier vrijgegeven.
    const [leaving, setLeaving] = useState(false);
    const [pending, setPending] = useState<Pending | null>(null);
    const state: GuardState = leaving ? 'none' : computed;
    const stateRef = useRef(state);
    stateRef.current = state;
    const armed = useRef(false);
    const skipPop = useRef(false);
    const backButton = useRef<HTMLButtonElement>(null);

    const api = useMemo<LeaveGuardApi>(() => ({
        set: (id, dirty, error) => { entries.current.set(id, { dirty, error }); setComputed(guardStateOf(entries.current.values())); },
        remove: (id) => { entries.current.delete(id); setComputed(guardStateOf(entries.current.values())); },
    }), []);

    useEffect(() => { setLeaving(false); }, [pathname]);

    // Tabblad sluiten of herladen: de waarschuwing van de browser zelf.
    useEffect(() => {
        if (state === 'none') return;
        const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
        window.addEventListener('beforeunload', warn);
        return () => window.removeEventListener('beforeunload', warn);
    }, [state]);

    // Browserknop Terug: een extra regel in de geschiedenis met dezelfde pagina. Terug haalt die weg (zelfde pagina, niets verandert),
    // wij zetten haar terug en stellen de vraag. Wordt het formulier weer schoon (opgeslagen), dan gaat de extra regel er stil af.
    useEffect(() => {
        if (state !== 'none' && !armed.current) {
            window.history.pushState(window.history.state, '', window.location.href);
            armed.current = true;
        } else if (state === 'none' && armed.current) {
            armed.current = false;
            if (!leaving) { skipPop.current = true; window.history.back(); }
        }
    }, [state, leaving]);

    useEffect(() => {
        const onPop = () => {
            if (skipPop.current) { skipPop.current = false; return; }
            if (!armed.current || stateRef.current === 'none') return;
            window.history.pushState(window.history.state, '', window.location.href);
            setPending({ kind: 'back' });
        };
        const onClick = (event: MouseEvent) => {
            if (stateRef.current === 'none') return;
            const href = leavingHref(event, window.location);
            if (href === null) return;
            event.preventDefault();
            event.stopPropagation();
            setPending({ kind: 'href', href });
        };
        window.addEventListener('popstate', onPop);
        document.addEventListener('click', onClick, true);
        return () => { window.removeEventListener('popstate', onPop); document.removeEventListener('click', onClick, true); };
    }, []);

    useEffect(() => { if (pending !== null) backButton.current?.focus(); }, [pending]);

    const stay = useCallback(() => setPending(null), []);
    const discard = useCallback(() => {
        const target = pending;
        setPending(null);
        setLeaving(true);
        if (target?.kind === 'href') router.push(target.href);
        // Terug: voorbij de extra regel en de pagina zelf.
        else if (target?.kind === 'back') { armed.current = false; window.history.go(-2); }
    }, [pending, router]);

    return (
        <LeaveGuardContext.Provider value={api}>
            {children}
            {pending !== null && (
                // Onder de rondleiding (z-70); op een telefoon ruimte onderin, zodat de knoppen niet onder de zwevende knop Demo-assistent vallen.
                <div className="fixed inset-0 z-50 flex items-end justify-center bg-gray-900/40 p-0 sm:items-center sm:p-4"
                    onKeyDown={(event) => { if (event.key === 'Escape') { event.preventDefault(); stay(); } }}>
                    <div role="alertdialog" aria-modal="true" aria-labelledby="leave-guard-title" data-testid="leave-guard"
                        className="w-full max-w-md space-y-3 rounded-t-lg bg-white p-4 shadow-xl max-sm:pb-20 sm:rounded-lg">
                        <h2 id="leave-guard-title" className="text-base font-semibold text-gray-900">
                            {t(computed === 'error' ? 'leave_guard_error' : 'leave_guard_unsaved')}
                        </h2>
                        <p className="text-sm text-gray-700">{t('leave_guard_hint')}</p>
                        <div className="flex flex-wrap justify-end gap-2">
                            <button type="button" className={secondaryButtonClass} onClick={discard} data-testid="leave-guard-discard">{t('leave_guard_discard')}</button>
                            <button type="button" ref={backButton} className={primaryButtonClass} onClick={stay} data-testid="leave-guard-stay">{t('leave_guard_stay')}</button>
                        </div>
                    </div>
                </div>
            )}
        </LeaveGuardContext.Provider>
    );
}
