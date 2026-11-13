'use client';

import { KeyboardEvent, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type RecipientProblem, type RecipientRow, emptyRow, isBlankRow, typoHint } from '../../lib/documentRecipients';
import { DOCUMENT_KINDS, type DocumentKind, type RecipientAddressOption } from '../../types/documentMail';
import { inputClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// E-mailadressen voor documenten (documenten deel 3): per regel een naam, het adres, Aan of Cc, de documenten waarvoor het geldt en (bij
// een relatie met afleveradressen) of het voor de hele relatie of één afleveradres is. Tab op het laatste veld van de laatste regel maakt
// een nieuwe regel; de knoppen staan buiten de Tab-volgorde. Een bekende tikfout geeft een hint, nooit een blokkade.
export default function DocumentRecipientsEditor({ idPrefix, rows, onChange, addresses = null, problems = [], disabled = false, defaultKinds = [], dataTour }: {
    idPrefix: string;
    rows: RecipientRow[];
    onChange: (rows: RecipientRow[]) => void;
    addresses?: RecipientAddressOption[] | null;
    problems?: RecipientProblem[];
    disabled?: boolean;
    defaultKinds?: DocumentKind[];
    dataTour?: string;
}) {
    const { t } = useTranslation();
    const [focusKey, setFocusKey] = useState<string | null>(null);
    const container = useRef<HTMLDivElement | null>(null);
    useEffect(() => {
        if (focusKey === null) return;
        container.current?.querySelector<HTMLInputElement>(`[data-row="${focusKey}"] input[data-field="name"]`)?.focus();
        setFocusKey(null);
    }, [focusKey, rows]);

    const edit = (key: string, patch: Partial<RecipientRow>) => onChange(rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));
    const add = () => { const row = emptyRow(defaultKinds); onChange([...rows, row]); setFocusKey(row.key); };
    const remove = (key: string) => onChange(rows.filter((row) => row.key !== key));
    const toggleKind = (row: RecipientRow, kind: DocumentKind) => edit(row.key, { kinds: row.kinds.includes(kind) ? row.kinds.filter((k) => k !== kind) : DOCUMENT_KINDS.filter((k) => k === kind || row.kinds.includes(k)) });
    // Tab op het laatste veld van de laatste (ingevulde) regel: een nieuwe regel, zoals bij de orderregels.
    const tabOnLast = (event: KeyboardEvent<HTMLElement>, index: number) => {
        if (event.key !== 'Tab' || event.shiftKey || index !== rows.length - 1 || isBlankRow(rows[index])) return;
        event.preventDefault();
        add();
    };
    const problemFor = (index: number, field: RecipientProblem['field']) => problems.find((p) => p.index === index && p.field === field);
    const showScope = addresses !== null && addresses.length > 0;

    return (
        <div ref={container} className="space-y-2" data-tour={dataTour}>
            {rows.length === 0 && <p className="text-xs text-gray-600">{t('recipients_none')}</p>}
            {rows.map((row, index) => {
                const id = (field: string) => `${idPrefix}-${index}-${field}`;
                const emailProblem = problemFor(index, 'email');
                const hint = typoHint(row);
                return (
                    <fieldset key={row.key} data-row={row.key} className="grid grid-cols-1 gap-2 rounded border border-gray-200 p-2 md:grid-cols-12" disabled={disabled}>
                        <legend className="sr-only">{t('recipients_row', { number: String(index + 1) })}</legend>
                        <div className="md:col-span-2">
                            <label htmlFor={id('name')} className="text-xs font-medium text-gray-700">{t('recipients_name')}</label>
                            <input id={id('name')} data-field="name" value={row.name} maxLength={200} onChange={(event) => edit(row.key, { name: event.target.value })} className={inputClass} />
                        </div>
                        <div className="md:col-span-3">
                            <label htmlFor={id('email')} className="text-xs font-medium text-gray-700">{t('recipients_email')}</label>
                            <input id={id('email')} data-field="email" type="email" inputMode="email" autoComplete="off" value={row.email} maxLength={254} aria-invalid={emailProblem !== undefined}
                                onChange={(event) => edit(row.key, { email: event.target.value })} className={inputClass} data-tour={index === 0 ? `${dataTour}-email` : undefined} />
                            {emailProblem && <p role="alert" className="text-xs text-red-800">{t(emailProblem.code === 'duplicate' ? 'recipients_problem_duplicate' : 'recipients_problem_email')}</p>}
                            {!emailProblem && hint && (
                                <p className="text-xs text-amber-900">{t('recipients_typo', { suggestion: hint })} <button type="button" tabIndex={-1} className="underline" onClick={() => edit(row.key, { email: hint })}>{t('recipients_typo_use')}</button></p>
                            )}
                        </div>
                        <div className="md:col-span-1">
                            <label htmlFor={id('role')} className="text-xs font-medium text-gray-700">{t('recipients_role')}</label>
                            <select id={id('role')} value={row.role} onChange={(event) => edit(row.key, { role: event.target.value as RecipientRow['role'] })} className={inputClass}>
                                <option value="To">{t('recipients_role_To')}</option>
                                <option value="Cc">{t('recipients_role_Cc')}</option>
                            </select>
                        </div>
                        <div className={showScope ? 'md:col-span-4' : 'md:col-span-6'}>
                            <span className="text-xs font-medium text-gray-700">{t('recipients_kinds')}</span>
                            <div className="flex flex-wrap gap-x-3 gap-y-1 text-sm">
                                {DOCUMENT_KINDS.map((kind, k) => (
                                    <label key={kind} className="flex items-center gap-1">
                                        <input type="checkbox" checked={row.kinds.includes(kind)} onChange={() => toggleKind(row, kind)}
                                            onKeyDown={!showScope && k === DOCUMENT_KINDS.length - 1 ? (event) => tabOnLast(event, index) : undefined}
                                            data-tour={index === 0 ? `${dataTour}-kind-${kind}` : undefined} />
                                        {t(`doc_kind_${kind}`)}
                                    </label>
                                ))}
                            </div>
                            {problemFor(index, 'kinds') && <p role="alert" className="text-xs text-red-800">{t('recipients_problem_kinds')}</p>}
                        </div>
                        {showScope && (
                            <div className="md:col-span-2">
                                <label htmlFor={id('scope')} className="text-xs font-medium text-gray-700">{t('recipients_scope')}</label>
                                <select id={id('scope')} value={row.scope} onChange={(event) => edit(row.key, { scope: event.target.value })} onKeyDown={(event) => tabOnLast(event, index)} className={inputClass}>
                                    <option value="">{t('recipients_scope_relation')}</option>
                                    {addresses!.map((address) => <option key={address.id} value={String(address.id)}>{address.text ?? `#${address.id}`}</option>)}
                                </select>
                            </div>
                        )}
                        <div className="flex items-end md:col-span-12 md:justify-end">
                            <button type="button" tabIndex={-1} className="text-xs text-red-800 underline" onClick={() => remove(row.key)}>{t('recipients_remove')}</button>
                        </div>
                    </fieldset>
                );
            })}
            <button type="button" className={`${secondaryButtonClass} text-xs`} disabled={disabled} onClick={add} data-tour={dataTour ? `${dataTour}-add` : undefined}>+ {t('recipients_add')}</button>
            {addresses !== null && <p className="text-xs text-gray-600">{t('recipients_hint')}</p>}
        </div>
    );
}
