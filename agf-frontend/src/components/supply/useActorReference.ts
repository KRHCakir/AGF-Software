'use client';

import { useCallback, useEffect, useState } from 'react';

// Tot er authenticatie is, vraagt de API een expliciete actor. We onthouden die per browser voor gemak (geen geheim).
const STORAGE_KEY = 'agf_actor_reference';

export function useActorReference(): [string, (value: string) => void] {
    const [actor, setActorState] = useState('');

    useEffect(() => {
        try {
            const stored = localStorage.getItem(STORAGE_KEY);
            if (stored) {
                setActorState(stored);
            }
        } catch {
            // localStorage niet beschikbaar: het veld blijft gewoon leeg.
        }
    }, []);

    const setActor = useCallback((value: string) => {
        setActorState(value);
        try {
            localStorage.setItem(STORAGE_KEY, value);
        } catch {
            // Niet onthouden is geen fout.
        }
    }, []);

    return [actor, setActor];
}
