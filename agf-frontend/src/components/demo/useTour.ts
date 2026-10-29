'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { isAllowedDemoHref } from '../../lib/demoAssistant';
import { todayInAmsterdam } from '../../lib/calendarDate';
import { TourRunner } from '../../lib/tour/runner';
import { TOUR_ACTOR, TOUR_REF_PREFIX, TOUR_SPEEDS } from '../../lib/tour/types';
import type { TourChapter, TourMode, TourSpeed, TourState, TourTiming } from '../../lib/tour/types';
import { readStoredActor, writeStoredActor } from '../supply/useActorReference';

// Koppelt de stapmotor aan Next (navigeren), de browser (onthouden gebruikersnaam) en het paneel. Instellingen,
// voortgang en het DEMO-TOUR-volgnummer zijn per browser onthouden gemak; zonder localStorage werkt alles gewoon.

const SEQUENCE_KEY = 'agf_demo_tour_seq';
const DONE_KEY = 'agf_demo_tour_done';
const SETTINGS_KEY = 'agf_demo_tour_settings';
// De eigen gebruikersnaam tijdens een hoofdstuk: zo komt hij ook terug als het tabblad midden in de rondleiding sluit.
const SAVED_ACTOR_KEY = 'agf_demo_tour_saved_actor';

function remove(key: string) {
    try {
        localStorage.removeItem(key);
    } catch {
        // Niets te doen.
    }
}

// Tijdens een hoofdstuk "Demo-assistent", met de eigen naam apart bewaard; daarna (of bij de volgende keer openen) terug.
const tourActor = {
    get: readStoredActor,
    set: (value: string | null) => {
        if (value === TOUR_ACTOR) {
            if (read(SAVED_ACTOR_KEY, (raw) => raw) === null) write(SAVED_ACTOR_KEY, JSON.stringify(readStoredActor()));
        } else {
            remove(SAVED_ACTOR_KEY);
        }
        writeStoredActor(value);
    },
};

function restoreInterruptedActor() {
    const saved = read(SAVED_ACTOR_KEY, (raw) => { try { return { value: JSON.parse(raw) as string | null }; } catch { return { value: null }; } });
    if (saved === null) return;
    if (readStoredActor() === TOUR_ACTOR) writeStoredActor(saved.value);
    remove(SAVED_ACTOR_KEY);
}

export interface TourSettings {
    mode: TourMode;
    speed: TourSpeed;
}

const IDLE: TourState = { status: 'idle', chapterId: null, stepIndex: 0, stepCount: 0, mode: 'watch', speed: 'normal', target: null, error: null, vars: {} };

function read<T>(key: string, parse: (raw: string) => T | null): T | null {
    try {
        const raw = localStorage.getItem(key);
        return raw === null ? null : parse(raw);
    } catch {
        return null;
    }
}

function write(key: string, value: string) {
    try {
        localStorage.setItem(key, value);
    } catch {
        // Niet onthouden is geen fout.
    }
}

// DEMO-TOUR-001, -002, …: herkenbaar en terug te vinden in de referentievelden.
export function nextTourRef(): string {
    const current = read(SEQUENCE_KEY, (raw) => (/^\d{1,6}$/.test(raw) ? Number(raw) : null)) ?? 0;
    const next = current >= 999999 ? 1 : current + 1;
    write(SEQUENCE_KEY, String(next));
    return `${TOUR_REF_PREFIX}${String(next).padStart(3, '0')}`;
}

function parseSettings(raw: string): TourSettings | null {
    try {
        const value = JSON.parse(raw) as Partial<TourSettings>;
        const mode = value.mode === 'self' ? 'self' : 'watch';
        const speed = TOUR_SPEEDS.includes(value.speed as TourSpeed) ? (value.speed as TourSpeed) : 'normal';
        return { mode, speed };
    } catch {
        return null;
    }
}

function parseDone(raw: string): string[] | null {
    try {
        const value = JSON.parse(raw);
        return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : null;
    } catch {
        return null;
    }
}

// `timing` alleen voor tests (kortere wachttijden); in de app gelden de vaste snelheden.
export function useTour(allowWrites: boolean, timing?: (speed: TourSpeed) => TourTiming) {
    const router = useRouter();
    const routerRef = useRef(router);
    routerRef.current = router;
    // Het pad zoals Next het kent: de assistent zit in de layout en herrendert bij elke navigatie.
    const pathname = usePathname();
    const pathRef = useRef(pathname);
    pathRef.current = pathname;
    const [state, setState] = useState<TourState>(IDLE);
    const [settings, setSettingsState] = useState<TourSettings>({ mode: 'watch', speed: 'normal' });
    const [done, setDone] = useState<string[]>([]);
    const runnerRef = useRef<TourRunner | null>(null);

    useEffect(() => {
        setSettingsState(read(SETTINGS_KEY, parseSettings) ?? { mode: 'watch', speed: 'normal' });
        setDone(read(DONE_KEY, parseDone) ?? []);
        restoreInterruptedActor();
        const runner = new TourRunner({
            document: window.document,
            navigate: (href) => routerRef.current.push(href),
            currentPath: () => pathRef.current ?? '',
            isAllowedHref: isAllowedDemoHref,
            allowWrites,
            actor: tourActor,
            nextRef: nextTourRef,
            today: todayInAmsterdam,
            onChange: setState,
            timing,
        });
        runnerRef.current = runner;
        return () => {
            runner.dispose();
            runnerRef.current = null;
        };
    }, [allowWrites, timing]);

    // Een afgerond hoofdstuk krijgt een vinkje in de lijst.
    useEffect(() => {
        if (state.status !== 'done' || state.chapterId === null) return;
        const chapterId = state.chapterId;
        setDone((current) => {
            if (current.includes(chapterId)) return current;
            const next = [...current, chapterId];
            write(DONE_KEY, JSON.stringify(next));
            return next;
        });
    }, [state.status, state.chapterId]);

    const setSettings = useCallback((next: TourSettings) => {
        setSettingsState(next);
        write(SETTINGS_KEY, JSON.stringify(next));
        runnerRef.current?.setMode(next.mode);
        runnerRef.current?.setSpeed(next.speed);
    }, []);

    const start = useCallback((chapter: TourChapter) => {
        void runnerRef.current?.start(chapter, settings);
    }, [settings]);

    return { state, settings, setSettings, done, start, runner: runnerRef };
}
