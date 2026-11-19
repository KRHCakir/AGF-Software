'use client';

// Fixes 5b: Terug en Vooruit (popstate) moeten bij de weggaansbeveiliging (LeaveGuard) aankomen vóór de router van Next.js, die anders
// meteen de andere pagina toont. Chrome roept de luisteraars op `window` aan in de volgorde waarin ze zijn geregistreerd, ook met
// capture. Daarom registreert deze module haar luisteraar al bij het laden. De root-layout toont <HistoryFirst />, zodat de module bij
// elke binnenkomst geladen is voordat Next.js zijn eigen luisteraar registreert, ook als je via de startpagina binnenkomt. De luisteraar
// geeft door aan de bewaking die op dat moment actief is (setPopHandler); zonder bewaking doet hij niets.

type PopHandler = (event: PopStateEvent) => void;
interface HistoryFirstWindow { __agfPopHandler?: PopHandler | null; __agfPopRegistered?: boolean }

// Op window bewaard: na een herlaad van deze module (ontwikkelserver) blijft er één luisteraar, met de actuele afhandeling.
if (typeof window !== 'undefined') {
    const slot = window as unknown as HistoryFirstWindow;
    if (!slot.__agfPopRegistered) {
        slot.__agfPopRegistered = true;
        window.addEventListener('popstate', (event) => slot.__agfPopHandler?.(event));
    }
}

export function setPopHandler(handler: PopHandler | null): void {
    if (typeof window !== 'undefined') (window as unknown as HistoryFirstWindow).__agfPopHandler = handler;
}

export default function HistoryFirst() {
    return null;
}
