'use client';

import { createPortal } from 'react-dom';
import { useTranslation } from '../../i18n/LanguageContext';
import type { TourState } from '../../lib/tour/types';

// Zichtbare begeleiding van de rondleiding: een markeerkader om het element waar de assistent mee bezig is, een
// animatie-cursor die ernaartoe beweegt, en tijdens Kijk mee een doorzichtige laag die per ongeluk klikken op de pagina
// tegenhoudt. Via een portal in <body>, zodat het paneel (hogere z-index) erboven ligt en bedienbaar blijft; scrollen werkt gewoon.

export const TOUR_OVERLAY_Z = 'z-[60]';
export const TOUR_PANEL_Z = 'z-[70]';

const PADDING = 6;

export default function TourOverlay({ state }: { state: TourState }) {
    const { t } = useTranslation();
    const active = ['running', 'waiting', 'paused'].includes(state.status);
    if (!active) return null;
    const watching = state.mode === 'watch';
    const blocking = watching && state.status === 'running';
    const rect = state.target;
    return createPortal(
        <>
            {blocking && (
                <div data-testid="tour-blocker" aria-hidden="true" className={`fixed inset-0 ${TOUR_OVERLAY_Z} cursor-wait bg-transparent`}
                    title={t('demo_tour_blocker_hint')} onClick={(event) => event.preventDefault()} />
            )}
            {rect && (
                <div data-testid="tour-highlight" aria-hidden="true"
                    className={`pointer-events-none fixed ${TOUR_OVERLAY_Z} rounded-md ring-4 transition-all duration-300 ${state.status === 'waiting' ? 'animate-pulse ring-amber-400' : 'ring-blue-500'}`}
                    style={{ top: rect.top - PADDING, left: rect.left - PADDING, width: rect.width + PADDING * 2, height: rect.height + PADDING * 2 }} />
            )}
            {rect && watching && (
                <svg data-testid="tour-cursor" aria-hidden="true" width="22" height="22" viewBox="0 0 24 24"
                    className={`pointer-events-none fixed ${TOUR_OVERLAY_Z} drop-shadow transition-all duration-500 ease-out`}
                    style={{ top: rect.top + Math.min(rect.height / 2, 18), left: rect.left + Math.min(rect.width / 2, 40) }}>
                    <path d="M3 2l7.5 19 2.6-7.6L21 10.8z" fill="#1d4ed8" stroke="white" strokeWidth="1.5" strokeLinejoin="round" />
                </svg>
            )}
        </>,
        document.body,
    );
}
