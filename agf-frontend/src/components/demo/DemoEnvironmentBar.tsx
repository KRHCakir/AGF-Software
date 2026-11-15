'use client';

import { useEffect, useState } from 'react';
import { DEMO_BAR_TEXT, DEMO_BAR_TITLE, demoEnvironmentEnabled } from '../../lib/demoEnvironment';
import { getCurrentUser } from '../../services/currentUserService';

// De balk bovenaan elk scherm in de demo-omgeving, niet weg te klikken. Staat in de hoofdlayout, buiten de taalkeuze: de tekst is vast
// Nederlands. Achter Cloudflare Access ook het e-mailadres waarmee de klant binnenkwam (alleen weergave, uit api/me).
export default function DemoEnvironmentBar({ enabled = demoEnvironmentEnabled() }: { enabled?: boolean }) {
    const [accessUser, setAccessUser] = useState<string | null>(null);
    useEffect(() => {
        if (!enabled) return;
        const controller = new AbortController();
        getCurrentUser(controller.signal).then((user) => setAccessUser(user.accessUser ?? null)).catch(() => { /* zonder adres blijft de balk staan */ });
        return () => controller.abort();
    }, [enabled]);
    if (!enabled) return null;
    return (
        <div role="note" aria-label={DEMO_BAR_TITLE} data-testid="demo-environment-bar"
            className="flex flex-wrap items-center gap-x-3 gap-y-0.5 border-b border-amber-500 bg-amber-300 px-4 py-1.5 text-sm text-amber-950">
            <span><strong>{DEMO_BAR_TITLE}:</strong> {DEMO_BAR_TEXT}</span>
            {accessUser && <span className="text-amber-900" data-testid="demo-access-user">Toegang: {accessUser}</span>}
        </div>
    );
}
