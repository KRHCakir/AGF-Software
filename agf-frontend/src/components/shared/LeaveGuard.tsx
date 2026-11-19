'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { createContext, Suspense, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { historyIndexOf, setPopHandler } from './HistoryFirst';

// Fixes 5b, een fout blokkeert het weggaan. Een formulier meldt zich met useLeaveGuard(gewijzigd, fout). Zolang een formulier een
// invoerfout heeft of niet-opgeslagen wijzigingen, kan de gebruiker niet naar een andere pagina:
//   - een link (menu, submenu, keten-strip, knoppen die links zijn) vraagt eerst "Je formulier bevat nog een fout / niet-opgeslagen
//     wijzigingen." met Terug naar het formulier (standaard) en Annuleren en weggooien;
//   - de browserknoppen Terug en Vooruit (ook meerdere stappen via het geschiedenismenu): de provider vangt popstate op vóór Next.js,
//     zet de formulierpagina terug in de adresbalk en stelt dezelfde vraag;
//   - het tabblad sluiten of herladen: de eigen waarschuwing van de browser (beforeunload).
// Een onaangeroerd formulier blokkeert niets. Alleen weggaan van de pagina telt; binnen de pagina werken knoppen gewoon.

type GuardState = 'none' | 'dirty' | 'error';
// href: waar de gebruiker heen wilde. delta: bij Terug of Vooruit het aantal geschiedenisstappen vanaf de formulierregel (negatief = terug).
type Pending = { href: string; delta?: number };

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
type ClickEvent = Pick<MouseEvent, 'button' | 'metaKey' | 'ctrlKey' | 'shiftKey' | 'altKey' | 'defaultPrevented' | 'target'>;
type PageLocation = { href: string; origin: string; pathname: string; search: string };

function clickedUrl(event: ClickEvent, location: PageLocation): URL | null {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return null;
    const target = event.target as { closest?: (selector: string) => Element | null } | null;
    const anchor = typeof target?.closest === 'function' ? target.closest('a[href]') as HTMLAnchorElement | null : null;
    if (anchor === null || (anchor.target !== '' && anchor.target !== '_self') || anchor.hasAttribute('download')) return null;
    const url = new URL(anchor.getAttribute('href') ?? '', location.href);
    return url.origin === location.origin ? url : null;
}

export function leavingHref(event: ClickEvent, location: PageLocation): string | null {
    const url = clickedUrl(event, location);
    if (url === null || (url.pathname === location.pathname && url.search === location.search)) return null;
    return url.pathname + url.search + url.hash;
}

// useSearchParams vraagt een Suspense-grens; zo hangt alleen dit kleine onderdeel daarvan af, niet de hele layout.
function SearchWatcher({ onChange }: { onChange: () => void }) {
    const search = useSearchParams().toString();
    useEffect(() => { onChange(); }, [search, onChange]);
    return null;
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
    // De formulierpagina (adres en geschiedenisstand van Next.js) zolang er bewaakt wordt: daarheen terug als Terug of Vooruit weg wil.
    const formEntry = useRef<{ href: string; state: unknown } | null>(null);
    const restoring = useRef(false);
    // De vraag na Terug of Vooruit komt pas als de formulierpagina terug is (de herstel-popstate): anders kon weggooien de tweede sprong
    // vanaf de verkeerde regel starten (review #275).
    const afterRestore = useRef<Pending | null>(null);
    // Na weggooien bij Terug of Vooruit: de volgende popstate is de bedoelde sprong; die laten we door naar Next.js.
    const passThrough = useRef(false);
    const backButton = useRef<HTMLButtonElement>(null);

    const api = useMemo<LeaveGuardApi>(() => ({
        set: (id, dirty, error) => { entries.current.set(id, { dirty, error }); setComputed(guardStateOf(entries.current.values())); },
        remove: (id) => { entries.current.delete(id); setComputed(guardStateOf(entries.current.values())); },
    }), []);

    useEffect(() => { setLeaving(false); }, [pathname]);
    // Ook een andere zoekvraag op dezelfde pagina (?id=) is een nieuwe bestemming: de beveiliging gaat dan weer aan.
    const releaseLeaving = useCallback(() => setLeaving(false), []);

    // Tabblad sluiten of herladen: de waarschuwing van de browser zelf.
    useEffect(() => {
        if (state === 'none') return;
        const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
        window.addEventListener('beforeunload', warn);
        return () => window.removeEventListener('beforeunload', warn);
    }, [state]);

    // Elke keer dat er (nog steeds) bewaakt wordt de huidige pagina vastleggen; Next.js kan de geschiedenisstand intussen vervangen.
    useEffect(() => {
        formEntry.current = state === 'none' ? null : { href: window.location.href, state: window.history.state };
    });

    useEffect(() => {
        // Terug of Vooruit (popstate): de browser staat dan al op de andere regel. Via HistoryFirst komen wij vóór de luisteraar van
        // Next.js (die anders de andere pagina toont); wij houden die tegen, zetten de formulierpagina terug en stellen de vraag.
        // Een anker op dezelfde pagina (#sectie) blijft op dezelfde pagina en telt niet.
        const onPop = (event: PopStateEvent) => {
            if (passThrough.current) { passThrough.current = false; return; }
            if (restoring.current) {
                restoring.current = false;
                event.stopImmediatePropagation();
                if (afterRestore.current !== null) { setPending(afterRestore.current); afterRestore.current = null; }
                return;
            }
            const form = formEntry.current;
            if (stateRef.current === 'none' || form === null) return;
            const here = new URL(window.location.href);
            const there = new URL(form.href);
            if (here.pathname === there.pathname && here.search === there.search) return;
            event.stopImmediatePropagation();
            // Terug naar de formulierregel met history.go(): geen nieuwe regel, dus de vooruitgeschiedenis blijft intact. Die sprong geeft
            // zelf weer een popstate, die wij ook tegenhouden (Next.js heeft de andere pagina nooit getoond).
            const formIndex = historyIndexOf(form.state);
            const hereIndex = historyIndexOf(event.state ?? window.history.state);
            const href = here.pathname + here.search + here.hash;
            if (formIndex !== null && hereIndex !== null && formIndex !== hereIndex) {
                restoring.current = true;
                afterRestore.current = { href, delta: hereIndex - formIndex };
                window.history.go(formIndex - hereIndex);
            } else {
                // Geen volgnummers bekend: terugvallen op een nieuwe regel (dat wist de vooruitgeschiedenis). In de praktijk komt dit niet voor:
                // HistoryFirst nummert elke regel van dit document vanaf het laden, en regels van een vorig document geven geen popstate
                // (daar geldt de waarschuwing van de browser).
                window.history.pushState(form.state, '', form.href);
                setPending({ href });
            }
        };
        const onClick = (event: MouseEvent) => {
            if (stateRef.current === 'none') return;
            const href = leavingHref(event, window.location);
            if (href === null) return;
            event.preventDefault();
            event.stopPropagation();
            setPending({ href });
        };
        setPopHandler(onPop);
        document.addEventListener('click', onClick, true);
        return () => { setPopHandler(null); document.removeEventListener('click', onClick, true); };
    }, []);

    // Modaal: de focus gaat naar Terug naar het formulier, blijft met Tab binnen de vraag (focusval), en gaat bij Terug naar het
    // formulier weer naar het veld waar de gebruiker stond (review #275).
    const returnFocus = useRef<HTMLElement | null>(null);
    const dialogRef = useRef<HTMLDivElement>(null);
    useEffect(() => {
        if (pending === null) return;
        if (returnFocus.current === null && document.activeElement instanceof HTMLElement) returnFocus.current = document.activeElement;
        backButton.current?.focus();
    }, [pending]);
    const onDialogKey = (event: ReactKeyboardEvent) => {
        if (event.key === 'Escape') { event.preventDefault(); stay(); return; }
        if (event.key !== 'Tab') return;
        const buttons = Array.from(dialogRef.current?.querySelectorAll<HTMLButtonElement>('button') ?? []);
        if (buttons.length === 0) return;
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
        event.preventDefault();
        buttons[(index + (event.shiftKey ? buttons.length - 1 : 1)) % buttons.length].focus();
    };

    const stay = useCallback(() => {
        setPending(null);
        const target = returnFocus.current;
        returnFocus.current = null;
        if (target?.isConnected) setTimeout(() => target.focus(), 0);
    }, []);
    const discard = useCallback(() => {
        returnFocus.current = null;
        const target = pending;
        setPending(null);
        setLeaving(true);
        if (target === null) return;
        // Terug of Vooruit: dezelfde sprong nog eens, nu door naar Next.js. Zo blijft de geschiedenis precies zoals de browser haar had
        // (geen extra stap, vooruit blijft vooruit). Een link: gewoon naar die pagina.
        if (target.delta !== undefined) { passThrough.current = true; window.history.go(target.delta); }
        else router.push(target.href);
    }, [pending, router]);

    return (
        <LeaveGuardContext.Provider value={api}>
            <Suspense fallback={null}><SearchWatcher onChange={releaseLeaving} /></Suspense>
            {children}
            {pending !== null && (
                // Onder de rondleiding (z-70); op een telefoon ruimte onderin, zodat de knoppen niet onder de zwevende knop Demo-assistent vallen.
                <div className="fixed inset-0 z-50 flex items-end justify-center bg-gray-900/40 p-0 sm:items-center sm:p-4"
                    onKeyDown={onDialogKey}>
                    <div ref={dialogRef} role="alertdialog" aria-modal="true" aria-labelledby="leave-guard-title" data-testid="leave-guard"
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
