'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import type { TourChapter, TourMode, TourSpeed, TourState } from '../../lib/tour/types';
import { TOUR_SPEEDS } from '../../lib/tour/types';
import type { TourSettings } from './useTour';

// Tab Rondleiding met autopilot: hoofdstukkenlijst met voortgang, Kijk mee of Doe zelf, snelheid, en tijdens het afspelen
// de stap met "Nu doen we dit" en "Waarom" plus Pauze, Terug, Volgende en Stop. Alle teksten zijn i18n-sleutels.

export interface TourControls {
    pause: () => void;
    resume: () => void;
    back: () => void;
    next: () => void;
    stop: () => void;
    retry: () => void;
    doThisForMe: () => void;
}

const SPEED_KEYS: Record<TourSpeed, string> = { slow: 'demo_tour_speed_slow', normal: 'demo_tour_speed_normal', fast: 'demo_tour_speed_fast' };
const MODE_KEYS: Record<TourMode, string> = { watch: 'demo_tour_mode_watch', self: 'demo_tour_mode_self' };

const buttonClass = 'rounded border border-gray-300 bg-white px-3 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-40';
const primaryClass = 'rounded bg-blue-700 px-3 py-1 text-sm font-medium text-white hover:bg-blue-800 disabled:cursor-not-allowed disabled:opacity-40';
const segmentClass = (active: boolean) => `flex-1 rounded px-2 py-1 text-xs ${active ? 'bg-white font-medium text-blue-800 shadow-sm' : 'text-gray-600 hover:text-gray-900'}`;

export default function TourPanel({ chapters, state, settings, onSettings, done, onStart, controls, compact = false }: {
    chapters: readonly TourChapter[];
    state: TourState;
    settings: TourSettings;
    onSettings: (settings: TourSettings) => void;
    done: readonly string[];
    onStart: (chapter: TourChapter) => void;
    controls: TourControls;
    // Klein formaat als het paneel anders het gemarkeerde element afdekt: alleen de stap en de knoppen.
    compact?: boolean;
}) {
    const { t } = useTranslation();
    const chapter = chapters.find((candidate) => candidate.id === state.chapterId) ?? null;
    const playing = chapter !== null && ['checking', 'running', 'waiting', 'paused', 'failed'].includes(state.status);

    const settingsBlock = (
        <div className="space-y-2">
            <div>
                <p className="text-xs font-medium text-gray-700">{t('demo_tour_mode_label')}</p>
                <div className="mt-1 flex gap-1 rounded bg-gray-100 p-1">
                    {(['watch', 'self'] as const).map((mode) => (
                        <button key={mode} type="button" aria-pressed={settings.mode === mode} onClick={() => onSettings({ ...settings, mode })} className={segmentClass(settings.mode === mode)}>
                            {t(MODE_KEYS[mode])}
                        </button>
                    ))}
                </div>
                <p className="mt-1 text-xs text-gray-500">{t(settings.mode === 'watch' ? 'demo_tour_mode_watch_hint' : 'demo_tour_mode_self_hint')}</p>
            </div>
            <div>
                <p className="text-xs font-medium text-gray-700">{t('demo_tour_speed_label')}</p>
                <div className="mt-1 flex gap-1 rounded bg-gray-100 p-1">
                    {TOUR_SPEEDS.map((speed) => (
                        <button key={speed} type="button" aria-pressed={settings.speed === speed} onClick={() => onSettings({ ...settings, speed })} className={segmentClass(settings.speed === speed)}>
                            {t(SPEED_KEYS[speed])}
                        </button>
                    ))}
                </div>
            </div>
        </div>
    );

    if (!playing || chapter === null) {
        return (
            <div className="space-y-3">
                {state.status === 'done' && chapter && (
                    <div role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-green-900">
                        <p className="font-medium">{t('demo_tour_done', { chapter: t(chapter.titleKey) })}</p>
                        {chapter.doneKey && <p className="mt-1 text-sm">{t(chapter.doneKey, state.vars)}</p>}
                    </div>
                )}
                {state.status === 'stopped' && <p role="status" className="text-xs text-gray-600">{t('demo_tour_stopped')}</p>}
                <p>{t('demo_tour_autopilot_intro')}</p>
                {settingsBlock}
                <h3 className="text-xs font-semibold uppercase tracking-wide text-gray-500">{t('demo_tour_chapters')}</h3>
                <ol className="space-y-2">
                    {chapters.map((item, index) => {
                        const finished = done.includes(item.id);
                        return (
                            <li key={item.id} className="rounded border border-gray-200 px-3 py-2" data-testid={`tour-chapter-${item.id}`}>
                                <div className="flex flex-wrap items-start justify-between gap-2">
                                    <div className="min-w-0 flex-1">
                                        <p className="font-medium text-gray-900">
                                            {index + 1}. {t(item.titleKey)}
                                            {finished && <span className="ml-2 rounded bg-green-100 px-1.5 py-0.5 text-xs font-medium text-green-800">✓ {t('demo_tour_chapter_done')}</span>}
                                        </p>
                                        <p className="break-words text-xs text-gray-600">{t(item.comingSoon ? 'demo_tour_coming_soon' : item.summaryKey)}</p>
                                        {item.writes && !item.comingSoon && <p className="mt-1 text-xs text-amber-800">{t('demo_tour_writes_note')}</p>}
                                    </div>
                                    {!item.comingSoon && (
                                        <button type="button" onClick={() => onStart(item)} className={primaryClass}>{t(finished ? 'demo_tour_again' : 'demo_tour_start_chapter')}</button>
                                    )}
                                </div>
                            </li>
                        );
                    })}
                </ol>
            </div>
        );
    }

    const step = chapter.steps[state.stepIndex];
    if (state.status === 'failed' && state.error) {
        const precheck = state.error.missing !== undefined || ['tour_error_writes_disabled', 'tour_error_precheck_failed', 'tour_error_coming_soon'].includes(state.error.key);
        return (
            <div className="space-y-3" role="alert">
                <p className="text-xs text-gray-500">{t(chapter.titleKey)}{!precheck && ` · ${t('demo_step_counter', { current: String(state.stepIndex + 1), total: String(state.stepCount) })}`}</p>
                <h3 className="font-semibold text-red-800">{t(precheck ? 'demo_tour_precheck_title' : 'demo_tour_failed_title')}</h3>
                <p className="break-words text-red-900">{t(state.error.key, state.error.params)}</p>
                {state.error.missing && (
                    <ul className="list-disc space-y-1 pl-5 text-red-900">
                        {state.error.missing.map((item) => <li key={`${item.key}-${JSON.stringify(item.params ?? {})}`} className="break-words">{t(item.key, item.params)}</li>)}
                    </ul>
                )}
                <div className="flex flex-wrap gap-2">
                    {!precheck && <button type="button" onClick={controls.retry} className={primaryClass}>{t('demo_tour_retry')}</button>}
                    {!precheck && <button type="button" onClick={controls.back} className={buttonClass}>{t('demo_tour_back')}</button>}
                    {!precheck && <button type="button" onClick={controls.next} className={buttonClass}>{t('demo_tour_skip')}</button>}
                    <button type="button" onClick={controls.stop} className={`${buttonClass} ml-auto`}>{t('demo_tour_to_chapters')}</button>
                </div>
            </div>
        );
    }

    return (
        <div className="space-y-3">
            <p className="text-xs text-gray-500">{t(chapter.titleKey)} · {t('demo_step_counter', { current: String(state.stepIndex + 1), total: String(state.stepCount) })}</p>
            {state.status === 'checking' ? (
                <p role="status">{t('demo_tour_checking')}</p>
            ) : step && (
                <>
                    <h3 className="font-semibold text-gray-900">{t(step.titleKey)}</h3>
                    <p className="break-words"><span className="font-medium text-blue-800">{t('demo_tour_now')}</span> {t(step.whatKey, state.vars)}</p>
                    {!compact && <p className="break-words"><span className="font-medium text-gray-900">{t('demo_tour_why')}</span> {t(step.whyKey, state.vars)}</p>}
                </>
            )}
            <div aria-live="polite">
                {state.status === 'waiting' && <p className="rounded bg-amber-50 px-2 py-1 text-amber-900">{t('demo_tour_your_turn')}</p>}
                {state.status === 'paused' && <p className="rounded bg-gray-100 px-2 py-1 text-gray-700">{t('demo_tour_paused')}</p>}
            </div>
            {state.mode === 'self' && (state.status === 'waiting' || state.status === 'paused') && (
                <button type="button" onClick={controls.doThisForMe} className={`${buttonClass} w-full`}>{t('demo_tour_do_for_me')}</button>
            )}
            <div className="flex flex-wrap gap-2">
                {state.status === 'paused'
                    ? <button type="button" onClick={controls.resume} className={primaryClass}>{t('demo_tour_resume')}</button>
                    : <button type="button" onClick={controls.pause} disabled={state.status === 'checking'} className={primaryClass}>{t('demo_tour_pause')}</button>}
                <button type="button" onClick={controls.back} disabled={state.status === 'checking'} className={buttonClass}>{t('demo_tour_back')}</button>
                <button type="button" onClick={controls.next} disabled={state.status === 'checking'} className={buttonClass}>{t('demo_tour_next')}</button>
                <button type="button" onClick={controls.stop} className={`${buttonClass} ml-auto`}>{t('demo_tour_stop')}</button>
            </div>
            {!compact && <div className="flex flex-wrap items-center gap-2 border-t border-gray-100 pt-2 text-xs text-gray-600">
                <span>{t('demo_tour_speed_label')}</span>
                {TOUR_SPEEDS.map((speed) => (
                    <button key={speed} type="button" aria-pressed={settings.speed === speed} onClick={() => onSettings({ ...settings, speed })}
                        className={`rounded px-1.5 py-0.5 ${settings.speed === speed ? 'bg-blue-100 font-medium text-blue-800' : 'hover:bg-gray-100'}`}>
                        {t(SPEED_KEYS[speed])}
                    </button>
                ))}
            </div>}
        </div>
    );
}
