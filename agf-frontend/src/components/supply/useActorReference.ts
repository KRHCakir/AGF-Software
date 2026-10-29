'use client';

import { useCallback, useEffect, useState } from 'react';

// Tot er authenticatie is, vraagt de API een expliciete actor. We onthouden die per browser voor gemak (geen geheim).
export const ACTOR_STORAGE_KEY = 'agf_actor_reference';
// De rondleiding zet tijdens het afspelen "Demo-assistent" en daarna de eigen naam terug; open schermen volgen dat direct.
const ACTOR_CHANGED_EVENT = 'agf-actor-changed';

export function readStoredActor(): string | null {
    try {
        return localStorage.getItem(ACTOR_STORAGE_KEY);
    } catch {
        return null;
    }
}

export function writeStoredActor(value: string | null) {
    try {
        if (value === null) localStorage.removeItem(ACTOR_STORAGE_KEY);
        else localStorage.setItem(ACTOR_STORAGE_KEY, value);
    } catch {
        // Niet onthouden is geen fout.
    }
    window.dispatchEvent(new Event(ACTOR_CHANGED_EVENT));
}

export function useActorReference(): [string, (value: string) => void] {
    const [actor, setActorState] = useState('');

    useEffect(() => {
        const load = () => setActorState(readStoredActor() ?? '');
        load();
        window.addEventListener(ACTOR_CHANGED_EVENT, load);
        return () => window.removeEventListener(ACTOR_CHANGED_EVENT, load);
    }, []);

    const setActor = useCallback((value: string) => {
        setActorState(value);
        writeStoredActor(value);
    }, []);

    return [actor, setActor];
}
