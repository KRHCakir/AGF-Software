'use client';

import { useCallback, useEffect, useState } from 'react';
import { type SortState, nextSort, readStoredSort, storeSort } from '../../lib/listSort';

const browserStorage = () => (typeof window === 'undefined' ? undefined : window.localStorage);

// De gekozen sortering van één scherm, onthouden in deze browser (localStorage onder agf_sort_<scherm>). De bewaarde stand wordt na
// het laden gelezen (niet tijdens het renderen op de server); `ready` zegt dat dat gebeurd is, zodat een lijst die op de server bladert
// meteen in de juiste volgorde ophaalt. Zonder opslag werkt alles gewoon, alleen zonder onthouden.
// Review #316: de stand hoort bij het scherm waarvoor ze gelezen is. Wisselt het scherm (inkoop- en verkoop-partijen op één pagina),
// dan is `ready` onwaar en `sort` leeg tot de stand van het nieuwe scherm gelezen is; een onbekende kolom wordt genegeerd.
export function useListSort(screen: string, keys: readonly string[]) {
    const [loaded, setLoaded] = useState<{ screen: string; sort: SortState | null } | null>(null);
    const allowed = keys.join('|');
    useEffect(() => {
        setLoaded({ screen, sort: readStoredSort(browserStorage, screen, allowed.split('|')) });
    }, [screen, allowed]);
    const ready = loaded?.screen === screen;
    const sort = ready ? loaded.sort : null;
    const setSort = useCallback((next: SortState | null) => {
        if (next !== null && !allowed.split('|').includes(next.key)) return;
        setLoaded({ screen, sort: next });
        storeSort(browserStorage, screen, next);
    }, [screen, allowed]);
    const toggle = useCallback((key: string) => {
        if (!allowed.split('|').includes(key)) return;
        setLoaded((current) => {
            const next = nextSort(current?.screen === screen ? current.sort : null, key);
            storeSort(browserStorage, screen, next);
            return { screen, sort: next };
        });
    }, [screen, allowed]);
    return { sort, ready, setSort, toggle };
}
