// Stapmotor van de rondleiding, los van React. Voert een hoofdstuk stap voor stap uit tegen het echte document:
// Kijk mee doet de acties zelf, Doe zelf markeert het element en wacht tot de gebruiker de actie heeft gedaan.
// Pauzeren, hervatten, overslaan, terug en stoppen werken op elk moment: elke wachttijd is een controlepunt, en een
// teller (epoch) breekt een lopende stap direct af. Terug start de vorige stap opnieuw; al opgeslagen gegevens blijven bestaan.

import {
    clearValue, findTarget, focus, interpolate, isDisabled, isEditable, normalizeText, pressKey, rectOf, scrollIntoView,
    setValueAtOnce, targetSelector, textOf, typeCharacter, typesAtOnce,
} from './dom.ts';
import { TOUR_ACTOR, TOUR_TIMING } from './types.ts';
import type {
    TourAction, TourChapter, TourError, TourMode, TourSpeed, TourState, TourStep, TourTarget, TourTiming,
} from './types.ts';

export interface TourRunnerOptions {
    document: Document;
    navigate: (href: string) => void;
    // Huidige pad met query (window.location), om te weten of navigeren nodig is en of het gelukt is.
    currentPath: () => string;
    isAllowedHref: (href: string) => boolean;
    // Schrijvende hoofdstukken alleen met NEXT_PUBLIC_DEMO_TOUR=true.
    allowWrites: boolean;
    // De onthouden gebruikersnaam (actorveld): tijdens de rondleiding "Demo-assistent", daarna terug.
    actor: { get: () => string | null; set: (value: string | null) => void };
    // Vaste herkenbare referentie voor alles wat de rondleiding aanmaakt (DEMO-TOUR-…).
    nextRef: () => string;
    today: () => string;
    onChange: (state: TourState) => void;
    timing?: (speed: TourSpeed) => TourTiming;
}

class Cancelled extends Error {}

class StepFailure extends Error {
    constructor(readonly tourError: TourError) {
        super(tourError.key);
    }
}

const pathname = (href: string) => href.split(/[?#]/)[0];

export class TourRunner {
    private readonly options: TourRunnerOptions;
    private state: TourState;
    private chapter: TourChapter | null = null;
    private epoch = 0;
    private paused = false;
    private pausedFrom: 'running' | 'waiting' = 'running';
    private resumeWaiters: (() => void)[] = [];
    private doForMe = false;
    private savedActor: { value: string | null } | null = null;
    private focused: HTMLElement | null = null;
    private precheck: AbortController | null = null;
    private clickedTargets = new Set<string>();
    private readonly onClick = (event: Event) => this.recordClick(event);
    private readonly onViewportChange = () => this.refreshTarget();
    // De pagina kan verschuiven zonder te scrollen (een melding verschijnt); het kader volgt het element dan ook.
    private refreshTimer: ReturnType<typeof setInterval> | null = null;

    constructor(options: TourRunnerOptions) {
        this.options = options;
        this.state = { status: 'idle', chapterId: null, stepIndex: 0, stepCount: 0, mode: 'watch', speed: 'normal', target: null, error: null, vars: {} };
    }

    getState(): TourState {
        return this.state;
    }

    get currentStep(): TourStep | null {
        return this.chapter?.steps[this.state.stepIndex] ?? null;
    }

    setSpeed(speed: TourSpeed) {
        this.emit({ speed });
    }

    setMode(mode: TourMode) {
        this.emit({ mode });
    }

    async start(chapter: TourChapter, settings: { mode: TourMode; speed: TourSpeed }): Promise<void> {
        this.cancel();
        this.chapter = chapter;
        const my = this.epoch;
        this.emit({ status: 'checking', chapterId: chapter.id, stepIndex: 0, stepCount: chapter.steps.length, mode: settings.mode, speed: settings.speed, target: null, error: null, vars: {} });
        if (chapter.comingSoon || chapter.steps.length === 0) {
            this.fail({ key: 'tour_error_coming_soon' });
            return;
        }
        if (chapter.writes && !this.options.allowWrites) {
            this.fail({ key: 'tour_error_writes_disabled' });
            return;
        }
        const vars: Record<string, string> = { actor: TOUR_ACTOR, today: this.options.today() };
        if (chapter.precheck) {
            const controller = new AbortController();
            this.precheck = controller;
            try {
                const result = await chapter.precheck(controller.signal);
                if (my !== this.epoch) return;
                if (result.missing.length > 0) {
                    this.fail({ key: 'tour_error_precheck', missing: result.missing });
                    return;
                }
                Object.assign(vars, result.vars ?? {});
            } catch {
                if (my !== this.epoch) return;
                this.fail({ key: 'tour_error_precheck_failed' });
                return;
            } finally {
                if (this.precheck === controller) this.precheck = null;
            }
        }
        if (chapter.writes) vars.tourRef = this.options.nextRef();
        this.useTourActor();
        this.listen();
        this.emit({ vars });
        void this.runFrom(0, my);
    }

    pause() {
        if (this.state.status !== 'running' && this.state.status !== 'waiting') return;
        this.pausedFrom = this.state.status;
        this.paused = true;
        this.emit({ status: 'paused' });
    }

    resume() {
        if (!this.paused) return;
        this.paused = false;
        this.emit({ status: this.pausedFrom });
        this.wake();
    }

    // Overslaan: de rest van deze stap niet doen en met de volgende beginnen.
    next() {
        if (!this.chapter || !this.isActive()) return;
        this.restartAt(this.state.stepIndex + 1);
    }

    // Terug: de vorige stap opnieuw (eerste stap: die opnieuw). Wat al is opgeslagen, wordt niet ongedaan gemaakt.
    back() {
        if (!this.chapter || !this.isActive()) return;
        this.restartAt(Math.max(0, this.state.stepIndex - 1));
    }

    // Na een mislukte stap (of halverwege): dezelfde stap opnieuw vanaf het begin.
    retry() {
        if (!this.chapter || !this.isActive()) return;
        this.restartAt(this.state.stepIndex);
    }

    // Doe zelf: "Doe dit stuk voor mij" voert de rest van de huidige stap automatisch uit.
    doThisForMe() {
        if (this.state.status === 'waiting' || this.state.status === 'paused') this.doForMe = true;
    }

    stop() {
        if (this.state.status === 'idle') return;
        this.cancel();
        this.finish('stopped');
    }

    dispose() {
        this.cancel();
        this.unlisten();
        this.restoreActor();
    }

    private isActive(): boolean {
        return ['running', 'waiting', 'paused', 'failed'].includes(this.state.status);
    }

    private restartAt(index: number) {
        const chapter = this.chapter!;
        this.cancel();
        if (index >= chapter.steps.length) {
            this.finish('done');
            return;
        }
        this.useTourActor();
        this.listen();
        void this.runFrom(index, this.epoch);
    }

    // Lopende stap afbreken: alle wachttijden zien de nieuwe epoch en stoppen.
    private cancel() {
        this.epoch++;
        this.paused = false;
        this.doForMe = false;
        this.precheck?.abort();
        this.precheck = null;
        this.wake();
    }

    private wake() {
        const waiters = this.resumeWaiters;
        this.resumeWaiters = [];
        waiters.forEach((resolve) => resolve());
    }

    private async runFrom(index: number, my: number): Promise<void> {
        const chapter = this.chapter!;
        try {
            for (let i = index; i < chapter.steps.length; i++) {
                await this.runStep(chapter.steps[i], i, my);
            }
            if (my === this.epoch) this.finish('done');
        } catch (caught) {
            if (caught instanceof Cancelled || my !== this.epoch) return;
            this.fail(caught instanceof StepFailure ? caught.tourError : { key: 'tour_error_unexpected', params: { detail: String((caught as Error)?.message ?? caught) } });
        }
    }

    private async runStep(step: TourStep, index: number, my: number) {
        this.doForMe = false;
        this.focused = null;
        this.emit({ status: 'running', stepIndex: index, target: null, error: null });
        if (pathname(this.options.currentPath()) !== pathname(step.route)) await this.navigateTo(step.route, my);
        await this.sleep(this.state.mode === 'watch' ? this.timing().readMs : this.timing().actionMs, my);
        for (const action of step.actions) {
            await this.perform(action, my);
        }
        if (step.expect) await this.assertTarget(step.expect.target, step.expect.text, undefined, my);
        await this.sleep(this.timing().actionMs, my);
    }

    private async perform(action: TourAction, my: number) {
        switch (action.kind) {
            case 'navigate':
                await this.navigateTo(action.href, my);
                return;
            case 'highlight':
                await this.point(await this.waitForTarget(action.target, my), my);
                await this.sleep(this.timing().actionMs, my);
                return;
            case 'wait':
                if (action.target) await this.waitForTarget(action.target, my, { text: action.text });
                else await this.sleep(action.ms ?? this.timing().actionMs, my);
                return;
            case 'assert':
                await this.assertTarget(action.target, action.text, action.value, my);
                return;
            case 'remember': {
                const element = await this.waitForTarget(action.target, my);
                const text = textOf(element).trim();
                const match = action.pattern ? action.pattern.exec(text) : null;
                const value = action.pattern ? match?.[1] ?? match?.[0] ?? null : text;
                if (!value) throw new StepFailure({ key: 'tour_error_remember', params: { target: this.text(action.target) } });
                this.emit({ vars: { ...this.state.vars, [action.name]: value } });
                return;
            }
            default:
                if (this.state.mode === 'self' && !this.doForMe) await this.userDoes(action, my);
                else await this.doAction(action, my);
                await this.sleep(this.timing().actionMs, my);
        }
    }

    private async doAction(action: Extract<TourAction, { target: TourTarget; kind: 'type' | 'pick' | 'select' | 'click' }>, my: number) {
        const element = await this.waitForTarget(action.target, my, { enabled: true });
        await this.point(element, my);
        switch (action.kind) {
            case 'click':
                element.click();
                return;
            case 'select': {
                if (!isEditable(element)) throw new StepFailure({ key: 'tour_error_not_field', params: { target: this.text(action.target) } });
                const value = this.text(action.value);
                setValueAtOnce(element, value);
                if (element.value !== value) throw new StepFailure({ key: 'tour_error_option', params: { value } });
                return;
            }
            case 'type':
                await this.typeInto(element, action.target, this.text(action.text), my);
                return;
            case 'pick': {
                const text = this.text(action.text);
                await this.typeInto(element, action.target, text, my);
                // De suggestielijst moet open zijn voordat Tab haar overneemt, zoals bij een gebruiker die even kijkt.
                await this.until(() => element.getAttribute('aria-expanded') === 'true', my, { key: 'tour_error_pick', params: { text } });
                await this.sleep(this.timing().actionMs, my);
                pressKey(element, action.key ?? 'Tab');
                const expect = action.expect ? this.text(action.expect) : null;
                await this.until(() => {
                    const current = findTarget(this.options.document, action.target, this.state.vars);
                    return current !== null && (current as HTMLInputElement).readOnly
                        && (expect === null || normalizeText(textOf(current)).includes(normalizeText(expect)));
                }, my, { key: 'tour_error_pick', params: { text } });
                return;
            }
        }
    }

    private async typeInto(element: HTMLElement, target: TourTarget, text: string, my: number) {
        if (!isEditable(element)) throw new StepFailure({ key: 'tour_error_not_field', params: { target: this.text(target) } });
        focus(element);
        if (typesAtOnce(element)) {
            setValueAtOnce(element, text);
            return;
        }
        const field = element as HTMLInputElement | HTMLTextAreaElement;
        clearValue(field);
        for (const character of text) {
            await this.sleep(this.timing().charMs, my);
            typeCharacter(field, character);
        }
    }

    // Doe zelf: het element markeren en wachten tot de gebruiker de actie heeft gedaan (of om hulp vraagt).
    private async userDoes(action: Extract<TourAction, { target: TourTarget; kind: 'type' | 'pick' | 'select' | 'click' }>, my: number) {
        const element = await this.waitForTarget(action.target, my);
        await this.point(element, my);
        this.clickedTargets.delete(action.target);
        this.emit({ status: 'waiting' });
        const done = () => {
            const current = findTarget(this.options.document, action.target, this.state.vars);
            if (!current) return false;
            switch (action.kind) {
                case 'click': return this.clickedTargets.has(action.target);
                case 'select': return textOf(current) === this.text(action.value);
                case 'type': return normalizeText(textOf(current)) === normalizeText(this.text(action.text));
                case 'pick': return (current as HTMLInputElement).readOnly
                    && normalizeText(textOf(current)).includes(normalizeText(this.text(action.expect ?? action.text)));
            }
        };
        while (!done()) {
            if (this.doForMe) {
                this.emit({ status: 'running' });
                await this.doAction(action, my);
                return;
            }
            await this.sleep(this.timing().pollMs, my);
        }
        this.emit({ status: 'running' });
    }

    private recordClick(event: Event) {
        const step = this.currentStep;
        if (!step || !(event.target instanceof this.view().Element)) return;
        for (const action of step.actions) {
            if (action.kind !== 'click') continue;
            if (event.target.closest(targetSelector(action.target, this.state.vars))) this.clickedTargets.add(action.target);
        }
    }

    private async navigateTo(href: string, my: number) {
        const target = this.text(href);
        if (!this.options.isAllowedHref(target)) throw new StepFailure({ key: 'tour_error_route', params: { href: target } });
        this.focused = null;
        this.emit({ target: null });
        this.options.navigate(target);
        await this.until(() => pathname(this.options.currentPath()) === pathname(target), my, { key: 'tour_error_route', params: { href: target } });
    }

    private async assertTarget(target: TourTarget, text: string | undefined, value: string | undefined, my: number) {
        const expected = text !== undefined ? this.text(text) : value !== undefined ? this.text(value) : null;
        const found = await this.find(target, my, (element) => expected === null || (value !== undefined
            ? textOf(element) === expected
            : normalizeText(textOf(element)).includes(normalizeText(expected))));
        if (!found) {
            throw new StepFailure(expected === null
                ? { key: 'tour_error_not_found', params: { target: this.text(target) } }
                : { key: 'tour_error_assert', params: { target: this.text(target), expected } });
        }
        await this.point(found, my);
    }

    private async waitForTarget(target: TourTarget, my: number, options: { text?: string; enabled?: boolean } = {}): Promise<HTMLElement> {
        const text = options.text !== undefined ? this.text(options.text) : null;
        const found = await this.find(target, my, (element) => (text === null || normalizeText(textOf(element)).includes(normalizeText(text)))
            && (!options.enabled || !isDisabled(element)));
        if (!found) throw new StepFailure(text === null ? { key: 'tour_error_not_found', params: { target: this.text(target) } } : { key: 'tour_error_assert', params: { target: this.text(target), expected: text } });
        return found;
    }

    // Wachten tot het element er is en aan de voorwaarde voldoet, hooguit de time-out van de snelheid.
    private async find(target: TourTarget, my: number, accept: (element: HTMLElement) => boolean): Promise<HTMLElement | null> {
        const deadline = Date.now() + this.timing().timeoutMs;
        for (;;) {
            await this.checkpoint(my);
            const element = findTarget(this.options.document, target, this.state.vars);
            if (element && accept(element)) return element;
            if (Date.now() >= deadline) return null;
            await this.sleep(this.timing().pollMs, my);
        }
    }

    private async until(condition: () => boolean, my: number, failure: TourError) {
        const deadline = Date.now() + this.timing().timeoutMs;
        while (!condition()) {
            if (Date.now() >= deadline) throw new StepFailure(failure);
            await this.sleep(this.timing().pollMs, my);
        }
    }

    // Het element in beeld brengen en markeren; de cursor beweegt ernaartoe.
    private async point(element: HTMLElement, my: number) {
        scrollIntoView(element);
        this.focused = element;
        await this.sleep(Math.round(this.timing().cursorMs / 2), my);
        this.emit({ target: rectOf(element) });
        await this.sleep(this.timing().cursorMs, my);
    }

    private refreshTarget() {
        if (!this.focused?.isConnected) return;
        const next = rectOf(this.focused);
        const current = this.state.target;
        if (current && current.top === next.top && current.left === next.left && current.width === next.width && current.height === next.height) return;
        this.emit({ target: next });
    }

    private async sleep(ms: number, my: number) {
        const end = Date.now() + ms;
        await this.checkpoint(my);
        while (Date.now() < end) {
            await new Promise((resolve) => setTimeout(resolve, Math.min(50, Math.max(0, end - Date.now()))));
            await this.checkpoint(my);
        }
    }

    private async checkpoint(my: number) {
        if (my !== this.epoch) throw new Cancelled();
        while (this.paused) {
            await new Promise<void>((resolve) => this.resumeWaiters.push(resolve));
            if (my !== this.epoch) throw new Cancelled();
        }
    }

    private timing(): TourTiming {
        return this.options.timing ? this.options.timing(this.state.speed) : TOUR_TIMING[this.state.speed];
    }

    private text(value: string): string {
        return interpolate(value, this.state.vars);
    }

    private view(): Window & typeof globalThis {
        return this.options.document.defaultView as Window & typeof globalThis;
    }

    private listen() {
        this.unlisten();
        this.options.document.addEventListener('click', this.onClick, true);
        this.view().addEventListener('scroll', this.onViewportChange, true);
        this.view().addEventListener('resize', this.onViewportChange);
        this.refreshTimer = setInterval(this.onViewportChange, 250);
    }

    private unlisten() {
        this.options.document.removeEventListener('click', this.onClick, true);
        this.view()?.removeEventListener('scroll', this.onViewportChange, true);
        this.view()?.removeEventListener('resize', this.onViewportChange);
        if (this.refreshTimer !== null) clearInterval(this.refreshTimer);
        this.refreshTimer = null;
    }

    // Tijdens de rondleiding staat "Demo-assistent" als gebruikersnaam (audit en historie); daarna de eigen naam weer.
    private useTourActor() {
        if (!this.chapter?.writes) return;
        if (this.savedActor === null) this.savedActor = { value: this.options.actor.get() };
        this.options.actor.set(TOUR_ACTOR);
    }

    private restoreActor() {
        if (this.savedActor === null) return;
        this.options.actor.set(this.savedActor.value);
        this.savedActor = null;
    }

    private fail(error: TourError) {
        this.focused = null;
        this.paused = false;
        this.restoreActor();
        this.emit({ status: 'failed', error, target: null });
    }

    private finish(status: 'done' | 'stopped') {
        this.focused = null;
        this.unlisten();
        this.restoreActor();
        this.emit({ status, target: null, error: null });
    }

    private emit(patch: Partial<TourState>) {
        this.state = { ...this.state, ...patch };
        this.options.onChange(this.state);
    }
}
