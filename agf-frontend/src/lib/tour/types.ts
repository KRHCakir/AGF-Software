// Rondleiding met autopilot: vaste scripts, geen AI. Een hoofdstuk is een reeks stappen; een stap toont "Nu doen we dit"
// en "Waarom" en voert daarna zijn acties uit (Kijk mee) of wacht tot de gebruiker ze zelf doet (Doe zelf).
// Elementen worden uitsluitend gevonden via data-tour="naam" (optioneel met data-tour-key="sleutel"), nooit via klassen of tekst.

export type TourSpeed = 'slow' | 'normal' | 'fast';
export const TOUR_SPEEDS: readonly TourSpeed[] = ['slow', 'normal', 'fast'];

export type TourMode = 'watch' | 'self';

// "naam" of "naam:sleutel"; in de sleutel en in teksten wordt {{variabele}} ingevuld (bijv. {{tourRef}}, {{orderNumber}}).
export type TourTarget = string;

// Elke actie kan `when` hebben: de actie wordt overgeslagen als die tekst (na invullen van {{variabelen}}) leeg is, bijv. een
// kenmerk dat bij dit artikel niet gevraagd wordt.
export type TourAction = TourActionKind & { when?: string };

export type TourActionKind =
    | { kind: 'navigate'; href: string }
    | { kind: 'highlight'; target: TourTarget }
    // Letter voor letter typen (datum- en keuzevelden in één keer), met echte invoer-events.
    | { kind: 'type'; target: TourTarget; text: string }
    // Typen in een zoekveld en de suggestie overnemen met Tab (of Enter), zoals een gebruiker; `expect` staat daarna in het veld.
    | { kind: 'pick'; target: TourTarget; text: string; expect?: string; key?: 'Tab' | 'Enter' }
    | { kind: 'select'; target: TourTarget; value: string }
    | { kind: 'click'; target: TourTarget }
    // Een vaste tijd, of tot het element er is (en eventueel de tekst bevat).
    | { kind: 'wait'; ms?: number; target?: TourTarget; text?: string }
    // Controle: staat de tekst of waarde in beeld? Zo niet, dan stopt de rondleiding met een duidelijke melding.
    | { kind: 'assert'; target: TourTarget; text?: string; value?: string }
    // De tekst van een element onthouden als variabele voor latere stappen (bijv. het nieuwe ordernummer).
    | { kind: 'remember'; target: TourTarget; name: string; pattern?: RegExp };

export interface TourStep {
    id: string;
    // De route waar de stap begint; staat de pagina elders, dan navigeert de rondleiding eerst (alleen toegestane routes).
    route: string;
    titleKey: string;
    whatKey: string;
    whyKey: string;
    actions: readonly TourAction[];
    // Eindcontrole van de stap (in beide manieren).
    expect?: { target: TourTarget; text?: string };
}

export interface TourMissing {
    key: string;
    params?: Record<string, string>;
}

export interface TourPrecheck {
    missing: TourMissing[];
    vars?: Record<string, string>;
}

export interface TourChapter {
    id: string;
    titleKey: string;
    summaryKey: string;
    steps: readonly TourStep[];
    // Korte samenvatting na afloop: wat staat er nu in het systeem.
    doneKey?: string;
    // Schrijft het hoofdstuk gegevens weg? Dan alleen met NEXT_PUBLIC_DEMO_TOUR=true.
    writes: boolean;
    // Leescontrole vooraf (gewone GET-aanroepen): ontbreekt er iets, dan start het hoofdstuk niet en zegt het wat.
    precheck?: (signal: AbortSignal) => Promise<TourPrecheck>;
    // Nog niet te bouwen: alleen een plek in de lijst.
    comingSoon?: boolean;
}

export type TourStatus = 'idle' | 'checking' | 'running' | 'paused' | 'waiting' | 'done' | 'failed' | 'stopped';

export interface TourRect {
    top: number;
    left: number;
    width: number;
    height: number;
}

export interface TourError {
    key: string;
    params?: Record<string, string>;
    missing?: TourMissing[];
}

export interface TourState {
    status: TourStatus;
    chapterId: string | null;
    stepIndex: number;
    stepCount: number;
    mode: TourMode;
    speed: TourSpeed;
    // Het element waar de rondleiding nu mee bezig is (markeerkader en cursor), in venstercoördinaten.
    target: TourRect | null;
    error: TourError | null;
    vars: Record<string, string>;
}

// Tijden per snelheid in milliseconden: per letter, tussen acties, en leestijd voordat een stap begint.
export interface TourTiming {
    charMs: number;
    actionMs: number;
    readMs: number;
    cursorMs: number;
    pollMs: number;
    timeoutMs: number;
}

export const TOUR_TIMING: Readonly<Record<TourSpeed, TourTiming>> = {
    slow: { charMs: 140, actionMs: 1300, readMs: 3500, cursorMs: 700, pollMs: 80, timeoutMs: 15000 },
    normal: { charMs: 70, actionMs: 700, readMs: 2000, cursorMs: 450, pollMs: 80, timeoutMs: 15000 },
    fast: { charMs: 20, actionMs: 250, readMs: 700, cursorMs: 200, pollMs: 60, timeoutMs: 15000 },
};

export const TOUR_ACTOR = 'Demo-assistent';
export const TOUR_REF_PREFIX = 'DEMO-TOUR-';
