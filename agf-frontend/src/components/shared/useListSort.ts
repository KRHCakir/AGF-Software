'use client';

import { useCallback, useEffect, useState } from 'react';
import { type SortState, nextSort, readStoredSort, storeSort } from '../../lib/listSort';

const browserStorage = () => (typeof window === 'undefined' ? undefined : window.localStorage);

// De gekozen sortering van één scherm, onthouden in deze browser (localStorage onder agf_sort_<scherm>). De bewaarde stand wordt na
// het laden gelezen (niet tijdens het renderen op de server); `ready` zegt dat dat gebeurd is, zodat een lijst die op de server bladert
// meteen in de juiste volgorde ophaalt. Zonder opslag werkt alles gewoon, alleen zonder onthouden.
export function useListSort(screen: string, keys: readonly string[]) {
    const [sort, setSortState] = useState<SortState | null>(null);
    const [ready, setReady] = useState(false);
    const allowed = keys.join('|');
    useEffect(() => {
        setSortState(readStoredSort(browserStorage, screen, allowed.split('|')));
        setReady(true);
    }, [screen, allowed]);
    const setSort = useCallback((next: SortState | null) => {
        setSortState(next);
        storeSort(browserStorage, screen, next);
    }, [screen]);
    const toggle = useCallback((key: string) => {
        setSortState((current) => {
            const next = nextSort(current, key);
            storeSort(browserStorage, screen, next);
            return next;
        });
    }, [screen]);
    return { sort, ready, setSort, toggle };
}
