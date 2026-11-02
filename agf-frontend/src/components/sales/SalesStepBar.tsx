'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { SALES_STEPS, salesNextStep } from '../../lib/salesNextStep';
import type { SalesOrder } from '../../types/salesOrder';

// De volgorde op een verkooporder in één regel: 1. Reserveren › 2. Verzenden › 3. Aflevering bevestigen, met de stap die nu aan de
// beurt is gemarkeerd en één zin wat je daarvoor doet. De knoppen zelf blijven waar ze staan (per regel reserveren, Verzenden in de kop).
export default function SalesStepBar({ order }: { order: SalesOrder }) {
    const { t } = useTranslation();
    const next = salesNextStep(order);
    if (next === 'cancelled') return null;
    return (
        <div className="rounded-md border border-blue-200 bg-blue-50 px-4 py-2 text-sm text-blue-950" data-tour="sales-steps" data-tour-key={next}>
            <ol className="flex flex-wrap items-center gap-x-2 gap-y-1">
                {SALES_STEPS.map((step, index) => {
                    const passed = next === 'done' || SALES_STEPS.indexOf(next as typeof step) > index;
                    return (
                        <li key={step} aria-current={next === step ? 'step' : undefined}
                            className={`flex items-center gap-1 ${next === step ? 'font-semibold' : passed ? 'text-green-800' : 'text-blue-900/70'}`}>
                            {index > 0 && <span aria-hidden="true" className="text-blue-400">›</span>}
                            <span>{passed ? '✓ ' : `${index + 1}. `}{t(`sales_step_${step}`)}</span>
                        </li>
                    );
                })}
            </ol>
            <p className="mt-0.5 text-xs">{t(`sales_next_${next}`)}</p>
        </div>
    );
}
