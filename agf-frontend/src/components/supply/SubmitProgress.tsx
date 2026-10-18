'use client';

import { useTranslation } from '../../i18n/LanguageContext';

export type StepState = 'waiting' | 'running' | 'done' | 'failed';

export interface ProgressStep {
    key: string;
    label: string;
    state: StepState;
}

const ICONS: Record<StepState, string> = { waiting: '○', running: '…', done: '✓', failed: '✕' };
const CLASSES: Record<StepState, string> = {
    waiting: 'text-gray-600',
    running: 'text-blue-800',
    done: 'text-green-800',
    failed: 'text-red-800',
};

// Voortgang per stap (pallet, daarna elke inhoudsregel), met tekst en icoon.
export default function SubmitProgress({ steps }: { steps: ProgressStep[] }) {
    const { t } = useTranslation();
    return (
        <ol aria-live="polite" className="space-y-1 rounded border border-gray-200 bg-white p-3 text-sm">
            {steps.map((step) => (
                <li key={step.key} className={`flex items-center gap-2 ${CLASSES[step.state]}`}>
                    <span aria-hidden="true" className="w-4 text-center">{ICONS[step.state]}</span>
                    <span>{step.label}</span>
                    <span className="text-xs">({t(`supply_step_${step.state}`)})</span>
                </li>
            ))}
        </ol>
    );
}
