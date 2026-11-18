'use client';

import { ReactNode, useEffect, useState } from 'react';
import { setWeightDecimals } from '../../lib/weightDisplay';
import { getWeightDecimals } from '../../services/weightDisplayService';

// Laadt één keer hoe het bedrijf gewichten toont (hele kilo's of één decimaal) en zet dat voor alle schermen (lib/weightDisplay.ts).
// De schermen wachten even op het antwoord, zodat een gewicht nooit eerst anders staat; antwoordt de API niet, dan hele kilo's.
export default function WeightDisplayProvider({ children }: { children: ReactNode }) {
    const [ready, setReady] = useState(false);
    useEffect(() => {
        const controller = new AbortController();
        getWeightDecimals(controller.signal).then(setWeightDecimals).catch(() => setWeightDecimals(0)).finally(() => { if (!controller.signal.aborted) setReady(true); });
        return () => controller.abort();
    }, []);
    return ready ? <>{children}</> : null;
}
