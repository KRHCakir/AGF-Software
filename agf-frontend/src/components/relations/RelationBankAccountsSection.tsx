'use client';

import { useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { checkIban, formatIban, normalizeIban } from '../../lib/iban';
import { BankAccountDraft, blankToNull, emptyBankAccountDraft } from '../../lib/relationRules';
import type { RelationBankAccountEvent } from '../../types/relation';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Bankrekeningen van de relatie. IBAN (vorm, land, lengte en controlegetal direct gecontroleerd) of, voor een land zonder IBAN, een
// ander rekeningnummer; BIC, houder, valuta, standaard en actief. Opgeslagen rekeningen worden nooit verwijderd, alleen inactief
// gezet. Elke nieuwe of gewijzigde rekening is "niet bevestigd" tot iemand haar (met naam, bijvoorbeeld na terugbellen) bevestigt:
// een geldige IBAN bewijst niet dat de rekening van deze relatie is. "Wie" is verplicht zodra een rekening wordt toegevoegd,
// haar IBAN/houder/nummer wijzigt of zij (in)actief wordt; de recente wijzigingen staan eronder.

const EVENTS_SHOWN = 5;

// Wijkt de invoer af van de opgeslagen rekening op een punt dat opnieuw bevestiging vraagt (IBAN, houder, ander nummer)?
function changedSinceSave(account: BankAccountDraft): boolean {
    const s = account.stored;
    if (s === null) return true;
    const iban = account.useOther ? null : normalizeIban(account.iban);
    const other = account.useOther ? blankToNull(account.otherAccountNumber) : null;
    return iban !== s.iban || other !== s.otherAccountNumber || account.accountHolder.trim() !== s.accountHolder;
}

export default function RelationBankAccountsSection({ accounts, onChange, actorReference, onActorChange, actorRequired, events, errors, locked, onConfirm, confirming, canConfirm }: {
    accounts: BankAccountDraft[];
    onChange: (accounts: BankAccountDraft[]) => void;
    actorReference: string;
    onActorChange: (value: string) => void;
    actorRequired: boolean;
    events: readonly RelationBankAccountEvent[];
    errors: FieldErrors;
    locked: boolean;
    onConfirm: (bankAccountId: number) => void;
    confirming: number | null;
    // Bevestigen kan alleen op een opgeslagen kaart zonder verouderd concept.
    canConfirm: boolean;
}) {
    const { t, locale } = useTranslation();
    const [showAllEvents, setShowAllEvents] = useState(false);
    const e = (key: string) => errors[key];
    const edit = (key: string, patch: Partial<BankAccountDraft>) => onChange(accounts.map((a) => (a.key === key ? { ...a, ...patch } : a)));
    // Eén actieve standaard: een andere rekening als standaard kiezen haalt het vinkje elders weg.
    const makeDefault = (key: string, value: boolean) => onChange(accounts.map((a) => (a.key === key ? { ...a, isDefault: value } : value ? { ...a, isDefault: false } : a)));
    const add = () => {
        const next = emptyBankAccountDraft();
        next.isDefault = !accounts.some((a) => a.isDefault && a.isActive);
        onChange([...accounts, next]);
        setTimeout(() => document.getElementById(`bankAccounts.${next.key}.iban-input`)?.focus(), 0);
    };
    const remove = (key: string) => onChange(accounts.filter((a) => a.key !== key));
    const time = (iso: string) => new Intl.DateTimeFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(iso));
    const accountLabel = (id: number) => {
        const stored = accounts.find((a) => a.id === id)?.stored;
        return stored ? (stored.iban ? formatIban(stored.iban) : stored.otherAccountNumber ?? `#${id}`) : `#${id}`;
    };
    const shownEvents = showAllEvents ? events : events.slice(0, EVENTS_SHOWN);

    return (
        <div className="space-y-2" data-testid="relation-bank-accounts">
            <FieldError id="bankAccounts" messages={e('bankAccounts')} />
            {accounts.length === 0 && <p className="text-xs text-gray-600">{t('relations_bank_empty')}</p>}
            {accounts.map((account) => {
                const k = (field: string) => `bankAccounts.${account.key}.${field}`;
                const normalized = normalizeIban(account.iban);
                const problem = account.useOther || normalized === null ? null : checkIban(normalized);
                const changed = changedSinceSave(account);
                const status = account.stored === null ? 'New' : changed ? 'WillBeUnconfirmed' : account.stored.confirmationStatus;
                const confirmable = canConfirm && account.id !== null && account.stored !== null && !changed && account.isActive && account.stored.isActive && account.stored.confirmationStatus === 'Unconfirmed';
                return (
                    <div key={account.key} className={`rounded border p-2 ${account.isActive ? 'border-gray-200' : 'border-gray-200 bg-gray-50'}`} data-testid={`bank-account-${account.id ?? account.key}`}>
                        <div className="mb-2 flex flex-wrap items-center gap-2">
                            <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${status === 'Confirmed' ? 'bg-green-100 text-green-900' : 'bg-amber-100 text-amber-950'}`} data-testid={`bank-status-${account.id ?? account.key}`}>
                                {t(`relations_bank_status_${status}`)}
                            </span>
                            {account.stored?.confirmationStatus === 'Confirmed' && !changed && account.stored.confirmedBy && account.stored.confirmedAt && (
                                <span className="text-xs text-gray-600">{t('relations_bank_confirmed_by', { name: account.stored.confirmedBy, time: time(account.stored.confirmedAt) })}</span>
                            )}
                            {account.isDefault && account.isActive && <span className="rounded bg-blue-100 px-1.5 py-0.5 text-xs font-medium text-blue-900">{t('relations_bank_default')}</span>}
                            {!account.isActive && <span className="rounded bg-gray-200 px-1.5 py-0.5 text-xs font-medium text-gray-800">{t('relations_inactive')}</span>}
                            {confirmable && (
                                <button type="button" className={`${secondaryButtonClass} border-amber-400 px-2 py-0.5 text-xs`} disabled={locked || confirming !== null || actorReference.trim() === ''}
                                    title={actorReference.trim() === '' ? t('relations_bank_confirm_needs_actor') : undefined} onClick={() => onConfirm(account.id!)} data-testid={`bank-confirm-${account.id}`}>
                                    {confirming === account.id ? t('supply_saving') : t('relations_bank_confirm')}
                                </button>
                            )}
                        </div>
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 md:grid-cols-6">
                            <div className="sm:col-span-2">
                                {account.useOther ? (
                                    <>
                                        <label htmlFor={`${k('otherAccountNumber')}-input`} className="text-xs font-medium text-gray-700">{t('relations_bank_other_number')} *</label>
                                        <input {...fieldProps(k('otherAccountNumber'), e(k('otherAccountNumber')))} value={account.otherAccountNumber} maxLength={64} disabled={locked} className={`${inputClass} font-mono`}
                                            onChange={(event) => edit(account.key, { otherAccountNumber: event.target.value })} />
                                        <FieldError id={k('otherAccountNumber')} messages={e(k('otherAccountNumber'))} />
                                    </>
                                ) : (
                                    <>
                                        <label htmlFor={`${k('iban')}-input`} className="text-xs font-medium text-gray-700">{t('relations_bank_iban')} *</label>
                                        <input {...fieldProps(k('iban'), e(k('iban')))} value={account.iban} maxLength={50} disabled={locked} autoComplete="off" spellCheck={false} className={`${inputClass} font-mono uppercase`}
                                            onChange={(event) => edit(account.key, { iban: event.target.value })} />
                                        {problem === 'none' && <span className="block text-xs text-green-800">✓ {formatIban(normalized!)} · {t('relations_bank_iban_ok')}</span>}
                                        <FieldError id={k('iban')} messages={e(k('iban'))} />
                                    </>
                                )}
                                <label className="mt-1 flex items-center gap-1 text-xs text-gray-700">
                                    <input type="checkbox" checked={account.useOther} disabled={locked} onChange={(event) => edit(account.key, { useOther: event.target.checked })} />
                                    {t('relations_bank_use_other')}
                                </label>
                            </div>
                            <div>
                                <label htmlFor={`${k('bic')}-input`} className="text-xs font-medium text-gray-700">{t('relations_bank_bic')}</label>
                                <input {...fieldProps(k('bic'), e(k('bic')))} value={account.bic} maxLength={14} disabled={locked} className={`${inputClass} font-mono uppercase`} onChange={(event) => edit(account.key, { bic: event.target.value })} />
                                <FieldError id={k('bic')} messages={e(k('bic'))} />
                            </div>
                            <div className="sm:col-span-2">
                                <label htmlFor={`${k('accountHolder')}-input`} className="text-xs font-medium text-gray-700">{t('relations_bank_holder')} *</label>
                                <input {...fieldProps(k('accountHolder'), e(k('accountHolder')))} value={account.accountHolder} maxLength={200} disabled={locked} className={inputClass} onChange={(event) => edit(account.key, { accountHolder: event.target.value })} />
                                <FieldError id={k('accountHolder')} messages={e(k('accountHolder'))} />
                            </div>
                            <div>
                                <label htmlFor={`${k('currency')}-input`} className="text-xs font-medium text-gray-700">{t('relations_bank_currency')}</label>
                                <input {...fieldProps(k('currency'), e(k('currency')))} value={account.currency} maxLength={3} list="relation-currencies" disabled={locked} className={`${inputClass} font-mono uppercase`} onChange={(event) => edit(account.key, { currency: event.target.value })} />
                                <FieldError id={k('currency')} messages={e(k('currency'))} />
                            </div>
                        </div>
                        <div className="mt-1 flex flex-wrap items-center gap-3 text-xs text-gray-800">
                            <label className="flex items-center gap-1"><input type="checkbox" id={`${k('isDefault')}-input`} checked={account.isDefault} disabled={locked} onChange={(event) => makeDefault(account.key, event.target.checked)} />{t('relations_bank_default')}</label>
                            <label className="flex items-center gap-1"><input type="checkbox" checked={account.isActive} disabled={locked} onChange={(event) => edit(account.key, { isActive: event.target.checked, ...(event.target.checked ? {} : { isDefault: false }) })} />{t('relations_active')}</label>
                            {account.stored === null && <button type="button" className="text-red-800 underline underline-offset-2" disabled={locked} onClick={() => remove(account.key)}>{t('relations_remove')}</button>}
                            <FieldError id={k('isDefault')} messages={e(k('isDefault'))} />
                        </div>
                        {changed && account.stored !== null && <p className="mt-1 text-xs text-amber-800">{t('relations_bank_reconfirm_hint')}</p>}
                    </div>
                );
            })}
            <div className="flex flex-wrap items-end gap-3">
                <button type="button" className={`${secondaryButtonClass} text-xs`} disabled={locked} onClick={add}>+ {t('relations_bank_add')}</button>
                <div className="min-w-48 flex-1 sm:max-w-xs">
                    <label htmlFor="actorReference-input" className="text-xs font-medium text-gray-700">{t('relations_bank_actor')}{actorRequired ? ' *' : ''}</label>
                    <input {...fieldProps('actorReference', e('actorReference'))} value={actorReference} maxLength={100} disabled={locked} autoComplete="name" className={inputClass} onChange={(event) => onActorChange(event.target.value)} />
                    <span className="block text-xs text-gray-500">{t(actorRequired ? 'relations_bank_actor_required_hint' : 'relations_bank_actor_hint')}</span>
                    <FieldError id="actorReference" messages={e('actorReference')} />
                </div>
            </div>
            <p className="text-xs text-gray-500">{t('relations_bank_hint')}</p>
            {events.length > 0 && (
                <div className="rounded border border-gray-200 bg-gray-50 px-2 py-1 text-xs text-gray-700" data-testid="bank-account-events">
                    <p className="font-medium">{t('relations_bank_events_title')}</p>
                    <ul className="mt-1 space-y-0.5">
                        {shownEvents.map((event) => (
                            <li key={event.id} className="break-words">
                                <span className="text-gray-600">{time(event.occurredAt)}</span> · {t(`relations_bank_event_${event.kind}`)} · {accountLabel(event.bankAccountId)} · {event.actorReference}
                                {event.kind === 'Changed' && event.oldIban !== event.newIban && <> · {event.oldIban ? formatIban(event.oldIban) : '–'} → {event.newIban ? formatIban(event.newIban) : '–'}</>}
                                {event.kind === 'Changed' && event.oldAccountHolder !== event.newAccountHolder && <> · {event.oldAccountHolder ?? '–'} → {event.newAccountHolder ?? '–'}</>}
                            </li>
                        ))}
                    </ul>
                    {events.length > EVENTS_SHOWN && (
                        <button type="button" className="mt-1 font-medium text-blue-800 underline underline-offset-2" onClick={() => setShowAllEvents((value) => !value)}>
                            {showAllEvents ? t('relations_bank_events_less') : t('relations_bank_events_all', { count: String(events.length) })}
                        </button>
                    )}
                </div>
            )}
        </div>
    );
}
