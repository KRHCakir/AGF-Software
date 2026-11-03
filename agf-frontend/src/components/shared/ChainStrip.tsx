'use client';

import Link from 'next/link';
import { Fragment } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import type { ChainPart, ChainState, ChainStep } from '../../lib/chain';
import { unitLabelKey } from '../../lib/units';
import { PACKAGE_UNITS, PackageUnit } from '../../types/availability';

const STATE_CLASS: Record<ChainState, string> = {
    done: 'border-green-300 bg-green-50',
    partial: 'border-amber-300 bg-amber-50',
    open: 'border-blue-300 bg-blue-50',
    empty: 'border-gray-200 bg-white',
    unavailable: 'border-dashed border-gray-300 bg-gray-50 text-gray-500',
};

// Smalle, klikbare balk bovenaan een document: Inkooporder > Ontvangst > Keuring > Voorraad > Verkooporder > Levering > Factuur. Per stap
// de status (kleur en tekst), de aantallen en links naar het scherm met het juiste filter. De stap van dit document is gemarkeerd
// (aria-current="step"). Loopt op een smal scherm door over meerdere regels; nooit een horizontale schuifbalk.
export default function ChainStrip({ steps, dataTour = 'chain-strip' }: { steps: readonly ChainStep[]; dataTour?: string }) {
    const { t, locale } = useTranslation();

    const unitText = (unit: string) => ((PACKAGE_UNITS as readonly string[]).includes(unit) ? t(unitLabelKey(unit as PackageUnit)).toLowerCase() : unit);
    const text = (part: ChainPart) => {
        const params: Record<string, string> = {};
        for (const [name, value] of Object.entries(part.params ?? {})) {
            params[name] = name === 'unit' ? unitText(String(value)) : typeof value === 'number' ? formatQuantity(value, locale) : value;
        }
        return t(part.key, params).replace(/\s+$/, '');
    };

    return (
        <nav aria-label={t('chain_label')} data-tour={dataTour} className="rounded-md border border-gray-200 bg-white p-2">
            <ol className="flex flex-wrap items-stretch gap-1 text-xs">
                {steps.map((step, index) => (
                    <Fragment key={step.key}>
                        {index > 0 && <li aria-hidden="true" className="flex items-center px-0.5 text-gray-400">›</li>}
                        <li aria-current={step.current ? 'step' : undefined} data-chain-step={step.key} data-chain-state={step.state} data-tour="chain-step" data-tour-key={step.key}
                            className={`min-w-0 rounded border px-2 py-1 ${STATE_CLASS[step.state]} ${step.current ? 'ring-2 ring-blue-500' : ''}`}>
                            <span className="block font-semibold text-gray-900">
                                {t(`chain_step_${step.key}`)}
                                <span className="sr-only"> · {t(`chain_state_${step.state}`)}</span>
                            </span>
                            {step.parts.length > 0 && <span className="block text-gray-700">{step.parts.map(text).join(' · ')}</span>}
                            {step.links.length > 0 && (
                                <span className="mt-0.5 flex flex-wrap gap-x-2">
                                    {step.links.map((link) => (
                                        <Link key={link.href} href={link.href} className="font-medium text-blue-800 underline underline-offset-2 hover:text-blue-900">
                                            {link.key ? t(link.key) : link.label}
                                        </Link>
                                    ))}
                                </span>
                            )}
                        </li>
                    </Fragment>
                ))}
            </ol>
        </nav>
    );
}
