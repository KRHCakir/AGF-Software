'use client';

import { usePathname } from 'next/navigation';
import { Fragment, ReactNode, useEffect, useRef, useState } from 'react';
import { setWeightDecimals } from '../../lib/weightDisplay';
import { getWeightDecimals } from '../../services/weightDisplayService';

// Laadt hoe het bedrijf gewichten toont (hele kilo's of één decimaal) en zet dat voor alle schermen (lib/weightDisplay.ts).
// Bij elk scherm dat wordt geopend wordt de instelling opnieuw gelezen, zodat een wijziging van een collega snel zichtbaar wordt.
// Alleen de eerste keer wachten de schermen op het antwoord (max. 5 seconden); faalt of hangt de API dan, dan hele kilo's.
// Een latere mislukte lezing laat de laatst bekende instelling staan.
const TIMEOUT_MS = 5000;

export default function WeightDisplayProvider({ children }: { children: ReactNode }) {
    const pathname = usePathname();
    const [ready, setReady] = useState(false);
    const [decimals, setDecimals] = useState(0);
    const loaded = useRef(false);
    useEffect(() => {
        const controller = new AbortController();
        let cancelled = false;
        const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
        getWeightDecimals(controller.signal)
            .then((d) => { if (!cancelled) { setWeightDecimals(d); setDecimals(d); loaded.current = true; } })
            .catch(() => { if (!cancelled && !loaded.current) { setWeightDecimals(0); setDecimals(0); } })
            .finally(() => { clearTimeout(timer); if (!cancelled) setReady(true); });
        return () => { cancelled = true; clearTimeout(timer); controller.abort(); };
    }, [pathname]);
    // De schermen lezen de globale formatter; de key laat ze opnieuw renderen als de instelling wijzigt.
    return ready ? <Fragment key={decimals}>{children}</Fragment> : null;
}
