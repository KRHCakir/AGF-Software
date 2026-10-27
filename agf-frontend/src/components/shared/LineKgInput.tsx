'use client';

import Link from 'next/link';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatDecimalInput } from '../../lib/decimal';
import { type KgField, type LineKgRow, type LineWeightDraft, isEntered, kgPerPackage } from '../../lib/lineWeight';
import { formatKgOneDecimal } from '../../lib/quantityKg';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';

// Eén kg-veld (netto of bruto) op een order- of aanvoerregel. Met een standaard van het artikel staat er live aantal × standaard
// in grijs ("berekend"); zelf typen maakt het zwart ("ingevuld") en dan volgt het het aantal niet meer; ↺ zet het terug.
// Zonder standaard is het veld leeg en optioneel. Begint met het (op een smal scherm zichtbare) label, net als de andere cellen.
export default function LineKgInput({ id, label, index, field, line, calculated, perPackage, locked, messages, onChange }: {
    // API-foutsleutel van het veld, bijvoorbeeld lines[0].netWeightKg; het invoerveld krijgt `${id}-input`.
    id: string;
    label: string;
    index: number;
    field: KgField;
    line: LineWeightDraft;
    // Aantal × standaard van het artikel (null = geen standaard of nog geen geldig aantal).
    calculated: number | null;
    // Standaard kg per verpakking, voor de uitleg "berekend · × 10 kg".
    perPackage: number | null;
    locked: boolean;
    messages: string[] | undefined;
    onChange: (patch: LineWeightDraft) => void;
}) {
    const { t, locale } = useTranslation();
    const textKey = field === 'net' ? 'netKg' : 'grossKg';
    const enteredKey = field === 'net' ? 'netKgEntered' : 'grossKgEntered';
    const entered = isEntered(line, field);
    const typing = line[enteredKey] === true;
    const shown = typing ? line[textKey] ?? '' : calculated !== null ? formatDecimalInput(calculated) : '';
    const state = entered ? 'entered' : calculated !== null ? 'calculated' : 'empty';

    return (
        <>
            <label htmlFor={`${id}-input`} className="sr-only">{label} {index + 1}</label>
            <div className="flex items-center gap-1">
                <input {...fieldProps(id, messages)} inputMode="decimal" value={shown} disabled={locked} data-weight-state={state}
                    onChange={(event) => onChange({ [textKey]: event.target.value, [enteredKey]: true })}
                    // Leeg gemaakt: terug naar de berekening (of gewoon leeg zonder standaard).
                    onBlur={() => { if (typing && (line[textKey] ?? '').trim() === '') onChange({ [textKey]: '', [enteredKey]: false }); }}
                    className={`${inputClass} !mt-0 min-w-[6.5rem] flex-1 text-right ${state === 'calculated' ? 'text-gray-500' : 'text-gray-900'}`} />
                {entered && calculated !== null && (
                    <button type="button" disabled={locked} onClick={() => onChange({ [textKey]: '', [enteredKey]: false })}
                        aria-label={t('line_kg_reset', { kg: formatDecimalInput(calculated) })} title={t('line_kg_reset', { kg: formatDecimalInput(calculated) })}
                        className="shrink-0 rounded border border-gray-300 px-1.5 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-40">↺</button>
                )}
            </div>
            {state !== 'empty' && (
                <span className={`block text-xs ${state === 'calculated' ? 'text-gray-500' : 'text-gray-900'}`} data-testid={`${id}-state`}>
                    {state === 'calculated' && perPackage !== null
                        ? t('line_kg_calculated_per', { perPackage: formatQuantity(perPackage, locale) })
                        : t(state === 'calculated' ? 'line_kg_calculated' : 'line_kg_entered')}
                </span>
            )}
            <FieldError id={id} messages={messages} />
        </>
    );
}

// Onder het netto-veld: zonder kg per verpakking bij het artikel een korte hint met een link naar het artikel; met zelf
// ingevulde netto kg de controle "= 10,0 kg per colli". Alleen weergave: het artikel wordt nooit aangepast.
export function LineKgHint({ variantId, hasDefault, netKg, quantity, unit, testId }: {
    variantId: number | null;
    hasDefault: boolean;
    netKg: number | null;
    quantity: number | null;
    unit: string;
    testId: string;
}) {
    const { t, locale } = useTranslation();
    const perPackage = hasDefault ? null : kgPerPackage(netKg, quantity);
    if (variantId === null || hasDefault) return null;
    return (
        <span className="block text-xs text-gray-600" data-testid={testId}>
            {perPackage !== null && <span className="block font-medium text-gray-900" data-testid={`${testId}-per-package`}>{t('line_kg_per_package', { kg: formatKgOneDecimal(perPackage, locale), unit })}</span>}
            {t('line_kg_no_default', { unit })}{' '}
            <Link href={`/articles?article=${variantId}`} target="_blank" rel="noopener" className="text-blue-700 underline decoration-dotted">{t('line_kg_open_article')}</Link>
        </span>
    );
}

// De twee kg-cellen van een regel (netto met hint, bruto) voor de regeltabel van een order- of aanvoerformulier.
// `netKey`/`grossKey` zijn de API-veldnamen (orders: netWeightKg, aanvoer: expectedNetWeightKg).
export function LineKgCells({ prefix, index, line, row, netKey, grossKey, unit, locked, errors, onChange }: {
    prefix: string;
    index: number;
    line: LineWeightDraft & { productVariantId: string };
    row: LineKgRow;
    netKey: string;
    grossKey: string;
    unit: string;
    locked: boolean;
    errors: (key: string) => string[] | undefined;
    onChange: (patch: LineWeightDraft) => void;
}) {
    const { t } = useTranslation();
    const netDefault = row.defaults?.defaultNetKgPerPackage ?? null;
    return (
        <>
            <td className="w-40 px-2 py-1.5">
                <LineKgInput id={`${prefix}.${netKey}`} label={t('line_kg_net')} index={index} field="net" line={line} calculated={row.calculated.net}
                    perPackage={netDefault} locked={locked} messages={errors(`${prefix}.${netKey}`)} onChange={onChange} />
                <LineKgHint variantId={line.productVariantId === '' ? null : Number(line.productVariantId)} hasDefault={netDefault !== null}
                    netKg={row.weight?.netWeightKg ?? null} quantity={row.quantity} unit={unit} testId={`${prefix}.kg-hint`} />
            </td>
            <td className="w-40 px-2 py-1.5">
                <LineKgInput id={`${prefix}.${grossKey}`} label={t('line_kg_gross')} index={index} field="gross" line={line} calculated={row.calculated.gross}
                    perPackage={row.defaults?.defaultGrossKgPerPackage ?? null} locked={locked} messages={errors(`${prefix}.${grossKey}`)} onChange={onChange} />
            </td>
        </>
    );
}
