'use client';

import { usePathname } from 'next/navigation';
import { ReactNode, useEffect, useRef, useState } from 'react';
import { applyQueuedWeightDecimals, queueWeightDecimals, setWeightDecimals, weightRevision } from '../../lib/weightDisplay';
import { getWeightDecimals } from '../../services/weightDisplayService';

// Laadt hoe het bedrijf gewichten toont (hele kilo's of één decimaal) en zet dat voor alle schermen (lib/weightDisplay.ts).
// - De eerste keer wachten de schermen op het antwoord (hooguit 5 seconden), zodat een gewicht nooit eerst anders staat; faalt of
//   hangt de API, dan hele kilo's.
// - Bij elk volgend scherm wordt de instelling op de achtergrond opnieuw gelezen. Een gewijzigde waarde (een collega paste haar
//   aan) geldt vanaf het scherm daarna: het scherm waarin iemand werkt, wordt nooit opnieuw opgebouwd en verliest geen invoer.
// - Een latere mislukte lezing laat de laatst bekende instelling staan.
const TIMEOUT_MS = 5000;

export default function WeightDisplayProvider({ children }: { children: ReactNode }) {
    const pathname = usePathname();
    const [ready, setReady] = useState(false);
    const [shownPath, setShownPath] = useState(pathname);
    const loaded = useRef(false);
    // Een nieuw scherm: eerst toepassen wat sinds het vorige scherm is gelezen, vóór de kinderen renderen.
    if (shownPath !== pathname) {
        setShownPath(pathname);
        applyQueuedWeightDecimals();
    }
    useEffect(() => {
        const controller = new AbortController();
        let cancelled = false;
        const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
        const since = weightRevision();
        getWeightDecimals(controller.signal)
            .then((decimals) => {
                if (cancelled) return;
                if (loaded.current) queueWeightDecimals(decimals, since);
                else setWeightDecimals(decimals);
                loaded.current = true;
            })
            .catch(() => { if (!cancelled && !loaded.current) { setWeightDecimals(0); loaded.current = true; } })
            .finally(() => { clearTimeout(timer); if (!cancelled) setReady(true); });
        return () => { cancelled = true; clearTimeout(timer); controller.abort(); };
    }, [pathname]);
    return ready ? <>{children}</> : null;
}
