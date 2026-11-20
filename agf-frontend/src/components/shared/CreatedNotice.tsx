'use client';

import { useCallback, useEffect, useState } from 'react';

// Fixes 5b, na opslaan van een nieuw item: het formulier sluit, de lijst ververst en toont het nieuwe item kort gemarkeerd, en deze melding
// blijft staan tot de gebruiker iets anders opent. `useCreated` houdt bij welk item nieuw is; de markering verdwijnt na FRESH_MS.
export const FRESH_MS = 8000;

export interface Created {
    id: number;
    text: string;
}

export function useCreated(): { created: Created | null; fresh: number | null; announce: (created: Created) => void; clear: () => void } {
    const [created, setCreated] = useState<Created | null>(null);
    const [fresh, setFresh] = useState<number | null>(null);
    useEffect(() => {
        if (fresh === null) return;
        const handle = setTimeout(() => setFresh(null), FRESH_MS);
        return () => clearTimeout(handle);
    }, [fresh]);
    const announce = useCallback((next: Created) => { setCreated(next); setFresh(next.id); }, []);
    const clear = useCallback(() => { setCreated(null); setFresh(null); }, []);
    return { created, fresh, announce, clear };
}

export default function CreatedNotice({ created, tour, onClose }: { created: Created | null; tour: string; onClose: () => void }) {
    if (created === null) return null;
    return (
        <div role="status" className="flex items-start justify-between gap-3 rounded border border-green-400 bg-green-100 px-3 py-2 text-sm text-green-950" data-tour={tour} data-testid="created-notice">
            <span>{created.text}</span>
            <button type="button" onClick={onClose} className="text-green-950 underline underline-offset-2" aria-label="×">×</button>
        </div>
    );
}
