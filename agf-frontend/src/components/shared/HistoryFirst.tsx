'use client';

// Fixes 5b: Terug en Vooruit (popstate) moeten bij de weggaansbeveiliging (LeaveGuard) aankomen vóór de router van Next.js, die anders
// meteen de andere pagina toont. Chrome roept de luisteraars op `window` aan in de volgorde waarin ze zijn geregistreerd, ook met
// capture. Daarom registreert deze module haar luisteraar al bij het laden. De root-layout toont <HistoryFirst />, zodat de module bij
// elke binnenkomst geladen is voordat Next.js zijn eigen luisteraar registreert, ook als je via de startpagina binnenkomt. De luisteraar
// geeft door aan de bewaking die op dat moment actief is (setPopHandler); zonder bewaking doet hij niets.

// Daarnaast krijgt elke geschiedenisregel die via pushState wordt toegevoegd een volgnummer (__agfIdx in de stand). Zo weet de bewaking
// hoeveel stappen Terug of Vooruit zijn gesprongen en kan zij met history.go() op de formulierregel terugkeren zonder iets te pushen
// (een push zou de vooruitgeschiedenis wissen). replaceState behoudt het volgnummer van de huidige regel.
const INDEX_KEY = '__agfIdx';

export function historyIndexOf(state: unknown): number | null {
    const value = typeof state === 'object' && state !== null ? (state as Record<string, unknown>)[INDEX_KEY] : undefined;
    return typeof value === 'number' ? value : null;
}

function withIndex(state: unknown, index: number): unknown {
    return typeof state === 'object' && state !== null ? { ...(state as object), [INDEX_KEY]: index } : state;
}

type PopHandler = (event: PopStateEvent) => void;
interface HistoryFirstWindow { __agfPopHandler?: PopHandler | null; __agfPopRegistered?: boolean; __agfHistoryPatched?: boolean }

// Op window bewaard: na een herlaad van deze module (ontwikkelserver) blijft er één luisteraar, met de actuele afhandeling.
if (typeof window !== 'undefined') {
    const slot = window as unknown as HistoryFirstWindow;
    if (!slot.__agfPopRegistered) {
        slot.__agfPopRegistered = true;
        window.addEventListener('popstate', (event) => slot.__agfPopHandler?.(event));
    }
    if (!slot.__agfHistoryPatched) {
        slot.__agfHistoryPatched = true;
        const push = window.history.pushState.bind(window.history);
        const replace = window.history.replaceState.bind(window.history);
        window.history.pushState = (state, unused, url) => {
            push(withIndex(state, (historyIndexOf(window.history.state) ?? 0) + 1), unused, url);
        };
        window.history.replaceState = (state, unused, url) => {
            replace(withIndex(state, historyIndexOf(window.history.state) ?? 0), unused, url);
        };
    }
}

export function setPopHandler(handler: PopHandler | null): void {
    if (typeof window !== 'undefined') (window as unknown as HistoryFirstWindow).__agfPopHandler = handler;
}

export default function HistoryFirst() {
    return null;
}
