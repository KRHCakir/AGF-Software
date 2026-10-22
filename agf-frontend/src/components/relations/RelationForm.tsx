'use client';

import { KeyboardEvent, useEffect, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { AddressDraft, ChannelDraft, ContactDraft, GgnDraft, RelationDraft, buildRelationPayload, draftFromRelation, emptyAddressDraft, emptyChannelDraft, emptyContactDraft, emptyGgnDraft, emptyRelationDraft, groupRelationErrors } from '../../lib/relationRules';
import { newRequestId } from '../../lib/requestId';
import { isMacPlatform, matchShortcut, shortcutLabel } from '../../lib/shortcuts';
import { ApiError, toApiError } from '../../services/apiClient';
import { addRelationRole, createRelation, updateRelation } from '../../services/relationService';
import type { AddressPurpose, ChannelKind, Relation, RelationReference, SaveRelationRequest, SimilarRelation } from '../../types/relation';
import { ADDRESS_PURPOSES } from '../../types/relation';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import type { FieldErrors } from '../supply/messages';
import { outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Relatiekaart: altijd zichtbaar zijn zoekcode, bedrijfsnaam, rollen, actief en de belangrijkste contactgegevens; de rest staat
// compact gegroepeerd (bedrijfsgegevens, adressen, contactpersonen, inkoop-/verkoopdefaults, interne opmerking). Alleen de naam
// is verplicht. Opslaan via ⌘S/Ctrl+S of de knop; Enter in een veld slaat niets op. Inline validatie met behoud van invoer.
// Aanmaken bevriest payload en requestId: bij een onbekende uitkomst wordt exact dezelfde opdracht herhaald (ook na verversen),
// zodat nooit twee relaties voor één invoer ontstaan. Gelijkende namen zijn een definitieve 409: "toch aanmaken" is een nieuwe opdracht.

const PENDING_KEY = 'agf_relation_pending_create';

interface PendingCreate { requestId: string; payload: SaveRelationRequest; draft: RelationDraft; lastError: string | null }

function readPending(): PendingCreate | null {
    try {
        const raw = window.sessionStorage.getItem(PENDING_KEY);
        return raw ? (JSON.parse(raw) as PendingCreate) : null;
    } catch {
        return null;
    }
}

function writePending(pending: PendingCreate | null) {
    try {
        if (pending === null) window.sessionStorage.removeItem(PENDING_KEY);
        else window.sessionStorage.setItem(PENDING_KEY, JSON.stringify(pending));
    } catch {
        // sessionStorage kan ontbreken (privémodus); de opdracht blijft dan alleen in het geheugen van dit scherm.
    }
}

const codeKey = (code: string) => `relations_error_${code}`;

export default function RelationForm({ relation, reference, onSaved, onClose }: {
    relation: Relation | null;
    reference: RelationReference;
    onSaved: (relation: Relation, created: boolean) => void;
    onClose: () => void;
}) {
    const { t } = useTranslation();
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    // De laatst opgeslagen kaart (rollen, versie, gebruik): na opslaan of een rol toevoegen leidend, ook als de ouder niet opnieuw rendert.
    const [current, setCurrent] = useState<Relation | null>(relation);
    const [pending, setPendingState] = useState<PendingCreate | null>(() => (relation === null ? readPending() : null));
    const [draft, setDraft] = useState<RelationDraft>(() => (relation !== null ? draftFromRelation(relation) : pending?.draft ?? emptyRelationDraft()));
    const [errors, setErrors] = useState<FieldErrors>({});
    const [serverError, setServerError] = useState<ApiError | null>(null);
    const [similar, setSimilar] = useState<SimilarRelation[] | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [notice, setNotice] = useState<string | null>(null);
    const [open, setOpen] = useState<Record<string, boolean>>(() => {
        const d = relation !== null ? draftFromRelation(relation) : emptyRelationDraft();
        return {
            company: d.website !== '' || d.vatNumber !== '' || d.registrationNumber !== '' || d.legalForm !== '' || d.preferredLanguage !== '',
            addresses: d.addresses.length > 0,
            contacts: d.contacts.length > 0,
            defaults: d.customerRole || d.vendorRole,
            note: d.internalNote !== '',
        };
    });
    const inFlight = useRef(false);
    const firstInput = useRef<HTMLInputElement | null>(null);
    useEffect(() => { firstInput.current?.focus(); }, []);

    const setPending = (next: PendingCreate | null) => { setPendingState(next); writePending(next); };
    const locked = submitting || pending !== null;

    const update = (patch: Partial<RelationDraft>) => {
        if (locked) return;
        setDraft((current) => ({ ...current, ...patch }));
        setErrors({});
        setServerError(null);
        setSimilar(null);
        setNotice(null);
    };
    const e = (key: string) => errors[key];
    const toggle = (section: string) => setOpen((current) => ({ ...current, [section]: !current[section] }));

    // Elke groep met een fout gaat open (een fout mag nooit verborgen blijven); het eerste foutveld krijgt focus.
    const sectionOf = (field: string): string | null =>
        field.startsWith('addresses') ? 'addresses' : field.startsWith('contacts') ? 'contacts' : field.startsWith('ggns') || field.includes('PaymentTerms') || field === 'vendorCurrency' ? 'defaults'
            : field === 'internalNote' ? 'note' : ['website', 'vatNumber', 'registrationNumber', 'registrationCountry', 'registrationType', 'legalForm', 'preferredLanguage'].includes(field) ? 'company' : null;
    const focusFirstError = (fields: string[]) => {
        const first = fields[0];
        if (!first) return;
        const sections = fields.map(sectionOf).filter((section): section is string => section !== null);
        if (sections.length > 0) setOpen((current) => ({ ...current, ...Object.fromEntries(sections.map((section) => [section, true])) }));
        setTimeout(() => document.getElementById(`${first}-input`)?.focus(), 0);
    };

    const applyServerError = (apiError: ApiError) => {
        setServerError(apiError);
        setErrors(apiError.fieldErrors);
        focusFirstError(Object.keys(apiError.fieldErrors));
    };

    const send = async (command: PendingCreate) => {
        inFlight.current = true;
        setSubmitting(true);
        setServerError(null);
        try {
            const result = await createRelation(command.payload);
            if (result.outcome === 'similar-names') {
                setPending(null);
                setSimilar(result.similar);
                return;
            }
            setPending(null);
            setCurrent(result.relation);
            setNotice(t(result.outcome === 'replayed' ? 'relations_saved_before' : 'relations_created', { name: result.relation.name }));
            setDraft(draftFromRelation(result.relation));
            onSaved(result.relation, true);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                // Uitkomst onbekend: dezelfde requestId en payload blijven bevroren; alleen exact dezelfde herhaling.
                setPending({ ...command, lastError: apiError.message });
                setServerError(apiError);
            } else {
                setPending(null);
                applyServerError(apiError);
            }
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    const submit = async (acknowledgeSimilarNames = false) => {
        if (inFlight.current) return;
        if (pending !== null) { await send(pending); return; }
        const built = buildRelationPayload(draft, current === null ? { requestId: newRequestId(), acknowledgeSimilarNames } : { expectedVersion: current.version });
        if (!built.ok) {
            const grouped = groupRelationErrors(built.errors);
            setErrors(Object.fromEntries(Object.entries(grouped).map(([field, codes]) => [field, codes.map((code) => t(codeKey(code)))])));
            focusFirstError(Object.keys(grouped));
            return;
        }
        setErrors({});
        setSimilar(null);
        if (current === null) {
            const command: PendingCreate = { requestId: built.value.requestId!, payload: built.value, draft, lastError: null };
            setPending(command);
            await send(command);
            return;
        }
        inFlight.current = true;
        setSubmitting(true);
        setServerError(null);
        try {
            const saved = await updateRelation(current.id, built.value);
            setCurrent(saved);
            setNotice(t('relations_saved', { name: saved.name }));
            setDraft(draftFromRelation(saved));
            onSaved(saved, false);
        } catch (caught) {
            applyServerError(toApiError(caught));
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    // Tweede rol toevoegen aan een bestaande relatie: één klik, nooit een tweede onderneming.
    const addRole = async (role: 'customer' | 'vendor') => {
        if (current === null || inFlight.current) return;
        inFlight.current = true;
        setSubmitting(true);
        try {
            const saved = await addRelationRole(current.id, role);
            setCurrent(saved);
            setNotice(t('relations_role_added', { name: saved.name }));
            // Het concept blijft staan: alleen de nieuwe rol en haar standaarden van de server komen erbij. Onopgeslagen wijzigingen
            // (naam, adressen, contacten, …) gaan pas met Opslaan mee, op de versie die de server nu teruggeeft.
            setDraft((draftNow) => role === 'customer'
                ? { ...draftNow, customerRole: true, customerPaymentTermsDays: String(saved.customer?.paymentTermsDays ?? 30) }
                : { ...draftNow, vendorRole: true, vendorPaymentTermsDays: String(saved.vendor?.paymentTermsDays ?? 30), vendorCurrency: saved.vendor?.defaultCurrency ?? 'EUR' });
            setOpen((current) => ({ ...current, defaults: true }));
            onSaved(saved, false);
        } catch (caught) {
            applyServerError(toApiError(caught));
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    // ⌘S/Ctrl+S slaat op; Enter in een invoerveld slaat niets op (geen ongewenste submit vanuit een lookup of lijst).
    const handler = useRef<(event: globalThis.KeyboardEvent) => void>(() => {});
    handler.current = (event: globalThis.KeyboardEvent) => {
        if (matchShortcut(event, isMac) === 'save') { event.preventDefault(); void submit(); }
    };
    useEffect(() => {
        const listener = (event: globalThis.KeyboardEvent) => handler.current(event);
        document.addEventListener('keydown', listener, true);
        return () => document.removeEventListener('keydown', listener, true);
    }, []);
    const stopEnter = (event: KeyboardEvent<HTMLElement>) => {
        if (event.key === 'Enter' && event.target instanceof HTMLInputElement) event.preventDefault();
    };

    // ---- rijen ----
    const editAddress = (key: string, patch: Partial<AddressDraft>) => update({ addresses: draft.addresses.map((a) => (a.key === key ? { ...a, ...patch } : a)) });
    const addAddress = (purpose: AddressPurpose) => {
        const next = emptyAddressDraft(purpose);
        next.isDefault = !draft.addresses.some((a) => a.purpose === purpose && a.isActive);
        update({ addresses: [...draft.addresses, next] });
        setOpen((current) => ({ ...current, addresses: true }));
        setTimeout(() => document.getElementById(`addresses.${next.key}.line1-input`)?.focus(), 0);
    };
    const removeAddress = (key: string) => update({ addresses: draft.addresses.filter((a) => a.key !== key) });
    const editContact = (key: string, patch: Partial<ContactDraft>) => update({ contacts: draft.contacts.map((c) => (c.key === key ? { ...c, ...patch } : c)) });
    const addContact = () => {
        const next = emptyContactDraft();
        next.isPrimary = !draft.contacts.some((c) => c.isPrimary && c.isActive);
        update({ contacts: [...draft.contacts, next] });
        setOpen((current) => ({ ...current, contacts: true }));
        setTimeout(() => document.getElementById(`contacts.${next.key}.name-input`)?.focus(), 0);
    };
    const removeContact = (key: string) => update({ contacts: draft.contacts.filter((c) => c.key !== key) });
    const editChannel = (contactKey: string, channelKey: string, patch: Partial<ChannelDraft>) =>
        update({ contacts: draft.contacts.map((c) => (c.key === contactKey ? { ...c, channels: c.channels.map((ch) => (ch.key === channelKey ? { ...ch, ...patch } : ch)) } : c)) });
    const addChannel = (contactKey: string, kind: ChannelKind) => {
        const next = emptyChannelDraft(kind);
        update({ contacts: draft.contacts.map((c) => (c.key === contactKey ? { ...c, channels: [...c.channels, next] } : c)) });
        setTimeout(() => document.getElementById(`contacts.${contactKey}.channels.${next.key}-input`)?.focus(), 0);
    };
    const removeChannel = (contactKey: string, channelKey: string) =>
        update({ contacts: draft.contacts.map((c) => (c.key === contactKey ? { ...c, channels: c.channels.filter((ch) => ch.key !== channelKey) } : c)) });
    const editGgn = (key: string, patch: Partial<GgnDraft>) => update({ ggns: draft.ggns.map((g) => (g.key === key ? { ...g, ...patch } : g)) });
    const addGgn = () => {
        const next = emptyGgnDraft();
        update({ ggns: [...draft.ggns, next] });
        setTimeout(() => document.getElementById(`ggns.${next.key}.ggn-input`)?.focus(), 0);
    };
    const removeGgn = (key: string) => update({ ggns: draft.ggns.filter((g) => g.key !== key) });

    const textField = (field: keyof RelationDraft & string, labelKey: string, options: { ref?: boolean; hintKey?: string; list?: string; mono?: boolean; maxLength?: number; span?: string; required?: boolean; upper?: boolean } = {}) => (
        <div className={options.span ?? ''}>
            <label htmlFor={`${field}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}{options.required ? ' *' : ''}</label>
            <input {...fieldProps(field, e(field))} ref={options.ref ? firstInput : undefined} value={String(draft[field])} list={options.list} maxLength={options.maxLength ?? 200} disabled={locked}
                onChange={(event) => update({ [field]: event.target.value } as Partial<RelationDraft>)} className={`${inputClass}${options.mono ? ' font-mono' : ''}${options.upper ? ' uppercase' : ''}`} />
            {options.hintKey && <span className="block text-xs text-gray-500">{t(options.hintKey)}</span>}
            <FieldError id={field} messages={e(field)} />
        </div>
    );

    const sectionButton = (key: string, labelKey: string, summary: string) => (
        <button type="button" aria-expanded={open[key]} aria-controls={`relation-${key}`} onClick={() => toggle(key)}
            className="flex w-full items-center justify-between px-3 py-2 text-left text-sm font-semibold text-gray-900">
            <span>{open[key] ? '▾' : '▸'} {t(labelKey)}{!open[key] && summary ? <span className="ml-2 font-normal text-gray-600">{summary}</span> : null}</span>
        </button>
    );

    const roleLabel = draft.customerRole && draft.vendorRole ? t('relations_role_both') : draft.customerRole ? t('relations_role_customer') : draft.vendorRole ? t('relations_role_vendor') : t('relations_role_none');
    const usage = current?.usage;

    return (
        <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }} onKeyDown={stopEnter} className="space-y-3 rounded-md border border-gray-300 bg-white p-3" data-testid="relation-form">
            <div className="flex flex-wrap items-start justify-between gap-2">
                <h2 className="text-lg font-semibold text-gray-900">{current === null ? t('relations_new') : t('relations_edit', { id: String(current.id) })}</h2>
                <div className="flex flex-wrap items-center gap-2">
                    <span className={`rounded px-2 py-0.5 text-xs font-medium ${draft.isActive ? 'bg-green-100 text-green-900' : 'bg-gray-200 text-gray-800'}`}>{draft.isActive ? t('relations_active') : t('relations_inactive')}</span>
                    <span className="rounded bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-900">{roleLabel}</span>
                    <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('relations_close')}</button>
                </div>
            </div>
            {notice && <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">{notice}</p>}
            {pending !== null && (
                <div role="alert" className="rounded border border-amber-400 bg-amber-50 px-3 py-2 text-sm text-amber-950">
                    <p className="font-medium">{t('relations_pending_title', { name: pending.payload.name })}</p>
                    {pending.lastError && <p className="mt-1 text-xs">{pending.lastError}</p>}
                    <p className="mt-1 text-xs">{t('relations_pending_hint')}</p>
                    <button type="button" className={`${primaryButtonClass} mt-2`} disabled={submitting} onClick={() => void submit()}>{t('supply_retry_same')}</button>
                </div>
            )}
            {serverError && pending === null && <ErrorNotice error={serverError} />}
            {similar !== null && (
                <div role="alert" className="rounded border border-amber-400 bg-amber-50 px-3 py-2 text-sm text-amber-950">
                    <p className="font-medium">{t('relations_similar_title')}</p>
                    <p className="mt-1 text-xs">{t('relations_similar_hint')}</p>
                    <ul className="mt-2 space-y-1">
                        {similar.map((candidate) => (
                            <li key={candidate.id} className="flex flex-wrap items-center gap-2">
                                <span className="font-mono text-xs">{candidate.searchCode ?? `#${candidate.id}`}</span>
                                <span className="font-medium">{candidate.name}</span>
                                <span className="text-xs">{candidate.customerId !== null && candidate.vendorId !== null ? t('relations_role_both') : candidate.customerId !== null ? t('relations_role_customer') : candidate.vendorId !== null ? t('relations_role_vendor') : t('relations_role_none')}</span>
                            </li>
                        ))}
                    </ul>
                    <button type="button" className={`${secondaryButtonClass} mt-2 border-amber-400`} onClick={() => void submit(true)}>{t('relations_create_anyway')}</button>
                </div>
            )}

            {/* Altijd zichtbaar: identificatie, rollen, actief, belangrijkste contactgegevens. */}
            <fieldset className="grid grid-cols-2 gap-3 md:grid-cols-4" disabled={locked}>
                <legend className="px-1 text-sm font-semibold text-gray-900">{t('relations_section_identity')}</legend>
                {textField('searchCode', 'relations_field_search_code', { ref: true, hintKey: 'relations_hint_search_code', mono: true, maxLength: 32 })}
                {textField('name', 'relations_field_name', { required: true, span: 'col-span-2' })}
                {textField('tradeName', 'relations_field_trade_name')}
                <div className="col-span-2 flex flex-wrap items-end gap-3 md:col-span-2">
                    <label className="flex items-center gap-1 text-sm text-gray-900">
                        <input type="checkbox" id="customerRole-input" checked={draft.customerRole} disabled={locked || current?.customer != null}
                            onChange={(event) => { update({ customerRole: event.target.checked }); setOpen((current) => ({ ...current, defaults: true })); }} />
                        {t('relations_role_customer')}
                    </label>
                    <label className="flex items-center gap-1 text-sm text-gray-900">
                        <input type="checkbox" id="vendorRole-input" checked={draft.vendorRole} disabled={locked || current?.vendor != null}
                            onChange={(event) => { update({ vendorRole: event.target.checked }); setOpen((current) => ({ ...current, defaults: true })); }} />
                        {t('relations_role_vendor')}
                    </label>
                    <label className="flex items-center gap-1 text-sm text-gray-900">
                        <input type="checkbox" id="isActive-input" checked={draft.isActive} disabled={locked} onChange={(event) => update({ isActive: event.target.checked })} />
                        {t('relations_active')}
                    </label>
                    {current !== null && !draft.isActive && <span className="text-xs text-gray-600">{t('relations_inactive_hint')}</span>}
                </div>
                {textField('email', 'relations_field_email', { maxLength: 200 })}
                {textField('phone', 'relations_field_phone', { maxLength: 40 })}
            </fieldset>

            {/* Bedrijfsgegevens */}
            <section className="rounded-md border border-gray-200">
                {sectionButton('company', 'relations_section_company', [draft.vatNumber, draft.legalForm, draft.registrationNumber].filter(Boolean).join(' · '))}
                {open.company && (
                    <div id="relation-company" className="grid grid-cols-2 gap-3 border-t border-gray-200 p-3 md:grid-cols-4">
                        <div>
                            <label htmlFor="preferredLanguage-input" className="text-xs font-medium text-gray-700">{t('relations_field_language')}</label>
                            <select {...fieldProps('preferredLanguage', e('preferredLanguage'))} value={draft.preferredLanguage} disabled={locked} className={inputClass} onChange={(event) => update({ preferredLanguage: event.target.value })}>
                                <option value="">–</option>
                                {reference.languages.map((language) => <option key={language} value={language}>{t(`relations_language_${language}`)}</option>)}
                            </select>
                            <FieldError id="preferredLanguage" messages={e('preferredLanguage')} />
                        </div>
                        {textField('website', 'relations_field_website')}
                        <div>
                            {textField('vatNumber', 'relations_field_vat', { mono: true, maxLength: 32, hintKey: 'relations_hint_vat' })}
                            <span className="mt-1 inline-flex rounded border border-gray-300 bg-gray-50 px-1.5 text-xs text-gray-700" data-testid="vat-status">
                                {t(`relations_vat_status_${current?.vatVerificationStatus ?? (draft.vatNumber.trim() === '' ? 'NotApplicable' : 'NotChecked')}`)}
                            </span>
                        </div>
                        {textField('legalForm', 'relations_field_legal_form', { list: 'relation-legal-forms', maxLength: 64 })}
                        <datalist id="relation-legal-forms">{reference.legalFormSuggestions.map((option) => <option key={option} value={option} />)}</datalist>
                        {textField('registrationNumber', 'relations_field_registration', { mono: true, maxLength: 64 })}
                        {textField('registrationCountry', 'relations_field_registration_country', { maxLength: 2, upper: true, hintKey: 'relations_hint_country' })}
                        {textField('registrationType', 'relations_field_registration_type', { list: 'relation-registration-types', maxLength: 32 })}
                        <datalist id="relation-registration-types">{reference.registrationTypeSuggestions.map((option) => <option key={option} value={option} />)}</datalist>
                    </div>
                )}
            </section>

            {/* Adressen */}
            <section className="rounded-md border border-gray-200">
                {sectionButton('addresses', 'relations_section_addresses', t('relations_count', { count: String(draft.addresses.length) }))}
                {open.addresses && (
                    <div id="relation-addresses" className="space-y-2 border-t border-gray-200 p-3">
                        <FieldError id="addresses" messages={e('addresses')} />
                        {draft.addresses.length === 0 && <p className="text-xs text-gray-600">{t('relations_addresses_empty')}</p>}
                        {draft.addresses.map((address) => {
                            const k = (field: string) => `addresses.${address.key}.${field}`;
                            const input = (field: 'label' | 'line1' | 'line2' | 'postalCode' | 'city' | 'region' | 'countryCode', labelKey: string, extra = '') => (
                                <div className={extra}>
                                    <label htmlFor={`${k(field)}-input`} className="text-xs font-medium text-gray-700">{t(labelKey)}{field === 'countryCode' ? ' *' : ''}</label>
                                    <input {...fieldProps(k(field), e(k(field)))} value={address[field]} disabled={locked} maxLength={field === 'countryCode' ? 2 : 300}
                                        onChange={(event) => editAddress(address.key, { [field]: event.target.value })} className={`${inputClass}${field === 'countryCode' ? ' uppercase' : ''}`} />
                                    <FieldError id={k(field)} messages={e(k(field))} />
                                </div>
                            );
                            return (
                                <div key={address.key} className="grid grid-cols-2 gap-2 rounded border border-gray-200 p-2 md:grid-cols-6" data-testid={`address-${address.key}`}>
                                    <div>
                                        <label htmlFor={`${k('purpose')}-input`} className="text-xs font-medium text-gray-700">{t('relations_address_purpose')}</label>
                                        <select id={`${k('purpose')}-input`} value={address.purpose} disabled={locked} className={inputClass} onChange={(event) => editAddress(address.key, { purpose: event.target.value as AddressPurpose })}>
                                            {ADDRESS_PURPOSES.map((purpose) => <option key={purpose} value={purpose}>{t(`relations_address_${purpose}`)}</option>)}
                                        </select>
                                    </div>
                                    {input('label', 'relations_address_label')}
                                    {input('line1', 'relations_address_line1', 'col-span-2')}
                                    {input('line2', 'relations_address_line2', 'col-span-2')}
                                    {input('postalCode', 'relations_address_postal')}
                                    {input('city', 'relations_address_city')}
                                    {input('region', 'relations_address_region')}
                                    {input('countryCode', 'relations_address_country')}
                                    <div className="col-span-2 flex flex-wrap items-center gap-3 text-xs text-gray-800">
                                        <label className="flex items-center gap-1"><input type="checkbox" checked={address.isDefault} disabled={locked} onChange={(event) => editAddress(address.key, { isDefault: event.target.checked })} />{t('relations_address_default')}</label>
                                        <label className="flex items-center gap-1"><input type="checkbox" checked={address.isActive} disabled={locked} onChange={(event) => editAddress(address.key, { isActive: event.target.checked })} />{t('relations_active')}</label>
                                        <button type="button" className="text-red-800 underline underline-offset-2" disabled={locked} onClick={() => removeAddress(address.key)}>{t('relations_remove')}</button>
                                    </div>
                                </div>
                            );
                        })}
                        <div className="flex flex-wrap gap-2">
                            {ADDRESS_PURPOSES.map((purpose) => <button key={purpose} type="button" className={`${secondaryButtonClass} text-xs`} disabled={locked} onClick={() => addAddress(purpose)}>+ {t(`relations_address_${purpose}`)}</button>)}
                        </div>
                        <p className="text-xs text-gray-500">{t('relations_addresses_hint')}</p>
                    </div>
                )}
            </section>

            {/* Contactpersonen */}
            <section className="rounded-md border border-gray-200">
                {sectionButton('contacts', 'relations_section_contacts', draft.contacts.filter((c) => c.isPrimary).map((c) => c.name).join(', ') || t('relations_count', { count: String(draft.contacts.length) }))}
                {open.contacts && (
                    <div id="relation-contacts" className="space-y-2 border-t border-gray-200 p-3">
                        <FieldError id="contacts" messages={e('contacts')} />
                        {draft.contacts.length === 0 && <p className="text-xs text-gray-600">{t('relations_contacts_empty')}</p>}
                        {draft.contacts.map((contact) => {
                            const k = (field: string) => `contacts.${contact.key}.${field}`;
                            return (
                                <div key={contact.key} className="rounded border border-gray-200 p-2" data-testid={`contact-${contact.key}`}>
                                    <div className="grid grid-cols-2 gap-2 md:grid-cols-4">
                                        <div>
                                            <label htmlFor={`${k('name')}-input`} className="text-xs font-medium text-gray-700">{t('relations_contact_name')} *</label>
                                            <input {...fieldProps(k('name'), e(k('name')))} value={contact.name} disabled={locked} maxLength={100} onChange={(event) => editContact(contact.key, { name: event.target.value })} className={inputClass} />
                                            <FieldError id={k('name')} messages={e(k('name'))} />
                                        </div>
                                        <div>
                                            <label htmlFor={`${k('department')}-input`} className="text-xs font-medium text-gray-700">{t('relations_contact_department')}</label>
                                            <input {...fieldProps(k('department'), e(k('department')))} value={contact.department} list="relation-departments" disabled={locked} maxLength={50} onChange={(event) => editContact(contact.key, { department: event.target.value })} className={inputClass} />
                                            <FieldError id={k('department')} messages={e(k('department'))} />
                                        </div>
                                        <div>
                                            <label htmlFor={`${k('jobTitle')}-input`} className="text-xs font-medium text-gray-700">{t('relations_contact_job')}</label>
                                            <input {...fieldProps(k('jobTitle'), e(k('jobTitle')))} value={contact.jobTitle} disabled={locked} maxLength={100} onChange={(event) => editContact(contact.key, { jobTitle: event.target.value })} className={inputClass} />
                                        </div>
                                        <div className="flex flex-wrap items-end gap-3 pb-1 text-xs text-gray-800">
                                            <label className="flex items-center gap-1"><input type="checkbox" checked={contact.isPrimary} disabled={locked} onChange={(event) => editContact(contact.key, { isPrimary: event.target.checked })} />{t('relations_contact_primary')}</label>
                                            <label className="flex items-center gap-1"><input type="checkbox" checked={contact.isActive} disabled={locked} onChange={(event) => editContact(contact.key, { isActive: event.target.checked })} />{t('relations_active')}</label>
                                            <button type="button" className="text-red-800 underline underline-offset-2" disabled={locked} onClick={() => removeContact(contact.key)}>{t('relations_remove')}</button>
                                        </div>
                                    </div>
                                    <div className="mt-2 space-y-1">
                                        {contact.channels.map((channel) => {
                                            const ck = `contacts.${contact.key}.channels.${channel.key}`;
                                            return (
                                                <div key={channel.key} className="flex flex-wrap items-start gap-2">
                                                    <select aria-label={t('relations_channel_kind')} value={channel.kind} disabled={locked} className={`${inputClass} max-w-32`} onChange={(event) => editChannel(contact.key, channel.key, { kind: event.target.value as ChannelKind })}>
                                                        <option value="Phone">{t('relations_channel_phone')}</option>
                                                        <option value="Email">{t('relations_channel_email')}</option>
                                                    </select>
                                                    <input aria-label={t('relations_channel_label')} placeholder={t('relations_channel_label')} value={channel.label} disabled={locked} maxLength={50} className={`${inputClass} max-w-40`} onChange={(event) => editChannel(contact.key, channel.key, { label: event.target.value })} />
                                                    <div className="min-w-48 flex-1">
                                                        <input {...fieldProps(ck, e(ck))} aria-label={channel.kind === 'Phone' ? t('relations_channel_phone') : t('relations_channel_email')} inputMode={channel.kind === 'Phone' ? 'tel' : 'email'} value={channel.value} disabled={locked} maxLength={200} className={inputClass}
                                                            onChange={(event) => editChannel(contact.key, channel.key, { value: event.target.value })} />
                                                        <FieldError id={ck} messages={e(ck)} />
                                                    </div>
                                                    <button type="button" tabIndex={-1} className="mt-1 text-xs text-red-800 underline underline-offset-2" disabled={locked} onClick={() => removeChannel(contact.key, channel.key)}>{t('relations_remove')}</button>
                                                </div>
                                            );
                                        })}
                                        <div className="flex gap-2">
                                            <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} disabled={locked} onClick={() => addChannel(contact.key, 'Phone')}>+ {t('relations_channel_phone')}</button>
                                            <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} disabled={locked} onClick={() => addChannel(contact.key, 'Email')}>+ {t('relations_channel_email')}</button>
                                        </div>
                                    </div>
                                </div>
                            );
                        })}
                        <datalist id="relation-departments">{reference.departmentSuggestions.map((option) => <option key={option} value={option} />)}</datalist>
                        <button type="button" className={`${secondaryButtonClass} text-xs`} disabled={locked} onClick={addContact}>+ {t('relations_contact_add')}</button>
                        <p className="text-xs text-gray-500">{t('relations_contacts_hint')}</p>
                    </div>
                )}
            </section>

            {/* Inkoop-/verkoopdefaults per rol */}
            <section className="rounded-md border border-gray-200">
                {sectionButton('defaults', 'relations_section_defaults', roleLabel)}
                {open.defaults && (
                    <div id="relation-defaults" className="space-y-3 border-t border-gray-200 p-3">
                        {!draft.customerRole && !draft.vendorRole && <p className="text-xs text-gray-600">{t('relations_defaults_no_role')}</p>}
                        {current !== null && (!current.customer || !current.vendor) && (
                            <div className="flex flex-wrap gap-2">
                                {!current.customer && <button type="button" className={`${secondaryButtonClass} text-xs`} disabled={locked} onClick={() => void addRole('customer')}>+ {t('relations_add_role_customer')}</button>}
                                {!current.vendor && <button type="button" className={`${secondaryButtonClass} text-xs`} disabled={locked} onClick={() => void addRole('vendor')}>+ {t('relations_add_role_vendor')}</button>}
                            </div>
                        )}
                        {draft.customerRole && (
                            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                                <p className="col-span-2 text-sm font-medium text-gray-900 md:col-span-4">{t('relations_defaults_sales')}{current?.customer ? ` · #${current.customer.customerId}` : ''}</p>
                                {textField('customerPaymentTermsDays', 'relations_field_payment_terms', { maxLength: 3, hintKey: 'relations_hint_customer_terms' })}
                            </div>
                        )}
                        {draft.vendorRole && (
                            <div className="space-y-2">
                                <p className="text-sm font-medium text-gray-900">{t('relations_defaults_purchase')}{current?.vendor ? ` · #${current.vendor.vendorId}` : ''}</p>
                                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                                    {textField('vendorPaymentTermsDays', 'relations_field_payment_terms', { maxLength: 3, hintKey: 'relations_hint_vendor_terms' })}
                                    {textField('vendorCurrency', 'relations_field_currency', { list: 'relation-currencies', maxLength: 3, upper: true, hintKey: 'relations_hint_currency' })}
                                    <datalist id="relation-currencies">{reference.currencies.map((option) => <option key={option} value={option} />)}</datalist>
                                </div>
                                <div className="space-y-1">
                                    <p className="text-xs font-medium text-gray-700">{t('relations_ggn_title')}</p>
                                    {draft.ggns.map((ggn) => {
                                        const k = (field: string) => `ggns.${ggn.key}.${field}`;
                                        return (
                                            <div key={ggn.key} className="flex flex-wrap items-start gap-2" data-testid={`ggn-${ggn.key}`}>
                                                <div>
                                                    <input {...fieldProps(k('ggn'), e(k('ggn')))} aria-label={t('relations_ggn_number')} placeholder={t('relations_ggn_number')} inputMode="numeric" value={ggn.ggn} disabled={locked} maxLength={20} className={`${inputClass} max-w-44 font-mono`}
                                                        onChange={(event) => editGgn(ggn.key, { ggn: event.target.value })} />
                                                    <FieldError id={k('ggn')} messages={e(k('ggn'))} />
                                                </div>
                                                <input {...fieldProps(k('producerName'), e(k('producerName')))} aria-label={t('relations_ggn_producer')} placeholder={t('relations_ggn_producer')} value={ggn.producerName} disabled={locked} maxLength={200} className={`${inputClass} min-w-48 flex-1`}
                                                    onChange={(event) => editGgn(ggn.key, { producerName: event.target.value })} />
                                                <label className="mt-2 flex items-center gap-1 text-xs text-gray-800"><input type="checkbox" checked={ggn.isActive} disabled={locked} onChange={(event) => editGgn(ggn.key, { isActive: event.target.checked })} />{t('relations_active')}</label>
                                                <button type="button" tabIndex={-1} className="mt-2 text-xs text-red-800 underline underline-offset-2" disabled={locked} onClick={() => removeGgn(ggn.key)}>{t('relations_remove')}</button>
                                            </div>
                                        );
                                    })}
                                    <div className="flex flex-wrap items-center gap-3">
                                        <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} disabled={locked} onClick={addGgn}>+ {t('relations_ggn_add')}</button>
                                        <a href={reference.globalGapDatabaseUrl} target="_blank" rel="noreferrer" className="text-xs font-medium text-blue-800 underline underline-offset-2">{t('relations_ggn_official')}</a>
                                    </div>
                                    <p className="text-xs text-gray-500">{t('relations_ggn_hint')}</p>
                                </div>
                            </div>
                        )}
                        {usage && (draft.customerRole || draft.vendorRole) && (
                            <div className="rounded border border-gray-200 bg-gray-50 px-2 py-1 text-xs text-gray-700" data-testid="relation-usage">
                                <p className="font-medium">{t('relations_usage_title')}</p>
                                <p>{t('relations_usage_counts', { salesOrders: String(usage.salesOrders), legacyOrders: String(usage.legacyOrders), directTrade: String(usage.directTradeCommitments), invoices: String(usage.purchaseInvoices), supplies: String(usage.supplyCommitments) })}</p>
                                <ul className="mt-1 list-disc pl-4">
                                    {usage.defaultsInUse.map((code) => <li key={code}>{t(`relations_usage_${code}`)}</li>)}
                                    {draft.customerRole && !usage.defaultsInUse.includes('customerPaymentTermsDaysLegacyOrders') && <li>{t('relations_usage_customerPaymentTermsDaysUnused')}</li>}
                                </ul>
                            </div>
                        )}
                    </div>
                )}
            </section>

            {/* Interne opmerking */}
            <section className="rounded-md border border-gray-200">
                {sectionButton('note', 'internal_note_label', draft.internalNote.trim() === '' ? '' : draft.internalNote.slice(0, 60))}
                {open.note && (
                    <div id="relation-note" className="border-t border-gray-200 p-3">
                        <label htmlFor="internalNote-input" className="text-xs font-medium text-gray-700">{t('internal_note_label')} <span className="rounded bg-gray-200 px-1 text-xs">{t('internal_note_badge')}</span></label>
                        <textarea {...fieldProps('internalNote', e('internalNote'))} rows={2} value={draft.internalNote} maxLength={1000} disabled={locked} onChange={(event) => update({ internalNote: event.target.value })} className={inputClass} />
                        <span className="block text-xs text-gray-500">{t('relations_note_hint')}</span>
                        <FieldError id="internalNote" messages={e('internalNote')} />
                    </div>
                )}
            </section>

            <div className="flex flex-wrap items-center gap-2 border-t border-gray-200 pt-3">
                <button type="submit" className={primaryButtonClass} disabled={locked}>{submitting ? t('supply_saving') : t('relations_save')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('save', isMac)}</kbd></button>
                <span className="text-xs text-gray-600">{t('relations_save_hint')}</span>
            </div>
        </form>
    );
}
