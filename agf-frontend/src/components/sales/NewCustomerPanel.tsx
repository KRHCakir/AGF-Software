'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { CustomerDraft, CustomerRuleError, MAX_CONTACT_PERSON_LENGTH, MAX_CUSTOMER_NAME_LENGTH, MAX_EMAIL_LENGTH, MAX_PHONE_LENGTH, MAX_SHIPPING_ADDRESS_LENGTH, buildCustomerPayload, emptyCustomerDraft, similarCustomers } from '../../lib/customerRules';
import { CREATE_CUSTOMER_KEY, CreateCustomerCommand, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { createCustomer } from '../../services/customerService';
import { CreatedCustomer, CustomerOption, SimilarCustomer } from '../../types/referenceData';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, addError, outcomeUnknown } from '../supply/messages';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand } from './PendingCommandsContext';

type Translate = (key: string, variables?: Record<string, string>) => string;

const MAX: Record<keyof CustomerDraft, number> = {
    name: MAX_CUSTOMER_NAME_LENGTH, contactPerson: MAX_CONTACT_PERSON_LENGTH, email: MAX_EMAIL_LENGTH, phone: MAX_PHONE_LENGTH, shippingAddress: MAX_SHIPPING_ADDRESS_LENGTH,
};

function ruleErrorsToFieldErrors(errors: CustomerRuleError[], t: Translate): FieldErrors {
    const found: FieldErrors = {};
    for (const error of errors) {
        const message = error.code === 'required' ? t('supply_error_required')
            : error.code === 'invalid_email' ? t('sales_customer_error_invalid_email')
            : t('supply_error_too_long', { max: String(MAX[error.field]) });
        addError(found, error.field, message);
    }
    return found;
}

const draftFromCommand = (command: CreateCustomerCommand): CustomerDraft => ({
    name: command.payload.name,
    contactPerson: command.payload.contactPerson ?? '',
    email: command.payload.email ?? '',
    phone: command.payload.phone ?? '',
    shippingAddress: command.payload.shippingAddress ?? '',
});

// Nieuwe klant vanuit het orderformulier (POST /api/customers). Het concept van de order staat buiten dit paneel en wordt
// hier nooit aangeraakt. Bij versturen worden payload en requestId bevroren: netwerk/5xx -> alleen exact dezelfde herhaling
// (ook na sluiten, navigeren of verversen; de banner houdt de opdracht zichtbaar). Een 409 met gelijkende klanten is een
// definitieve afwijzing: "Toch aanmaken" is een NIEUWE opdracht met een nieuwe requestId en de bevestigingsvlag aan.
export default function NewCustomerPanel({ customers, onCreated, onChooseExisting, onClose, initialName = '' }: {
    customers: CustomerOption[];
    // Getypte zoektekst uit de klantkeuze ("Nieuwe klant" bij geen treffer): alleen een voorinvulling van de naam.
    initialName?: string;
    onCreated: (customer: CreatedCustomer, replayed: boolean) => void;
    onChooseExisting: (customerId: number) => void;
    onClose: () => void;
}) {
    const { t } = useTranslation();
    const [pending, setPending, clearPending] = usePendingCommand<CreateCustomerCommand>(CREATE_CUSTOMER_KEY);
    const [draft, setDraft] = useState<CustomerDraft>(() => (pending === null ? { ...emptyCustomerDraft(), name: initialName } : draftFromCommand(pending)));
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const [similar, setSimilar] = useState<SimilarCustomer[] | null>(null);
    const inFlight = useRef(false);
    // Na ontkoppelen (paneel gesloten of weggenavigeerd tijdens het verzoek) wordt niets meer gekozen in een order:
    // de uitkomst wordt alleen nog in de opslag verwerkt.
    // StrictMode voert setup -> cleanup -> setup uit: de setup zet de vlag dus zelf weer op true.
    const mounted = useRef(true);
    useEffect(() => {
        mounted.current = true;
        return () => { mounted.current = false; };
    }, []);

    const locked = submitting || pending !== null;
    const liveSimilar = useMemo(() => (draft.name.trim().length >= 3 ? similarCustomers(draft.name, customers).slice(0, 5) : []), [draft.name, customers]);

    const edit = (field: keyof CustomerDraft, value: string) => {
        if (locked) return;
        setDraft((current) => ({ ...current, [field]: value }));
        setError(null);
        setFieldErrors({});
        setSimilar(null);
    };

    const send = async (command: CreateCustomerCommand) => {
        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const result = await createCustomer(command.payload);
            if (result.outcome === 'similar-names') {
                // Definitieve afwijzing: niets opgeslagen; de bevroren opdracht vervalt, bevestigen wordt een nieuwe opdracht.
                clearPending();
                if (mounted.current) setSimilar(result.similar);
                return;
            }
            clearPending();
            if (mounted.current) onCreated(result.customer, result.outcome === 'replayed');
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                // Uitkomst onbekend: identieke payload en requestId blijven bewaard; alleen exact dezelfde herhaling.
                setPending(withAttempt(command, apiError.message) as CreateCustomerCommand);
            } else {
                clearPending();
            }
            if (mounted.current) {
                setError(apiError);
                setFieldErrors(apiError.fieldErrors);
            }
        } finally {
            inFlight.current = false;
            if (mounted.current) setSubmitting(false);
        }
    };

    const freeze = (acknowledgeSimilarNames: boolean): CreateCustomerCommand | null => {
        const requestId = newRequestId();
        const built = buildCustomerPayload(draft, requestId, acknowledgeSimilarNames);
        if (!built.ok) {
            setFieldErrors(ruleErrorsToFieldErrors(built.errors, t));
            return null;
        }
        setFieldErrors({});
        const command: CreateCustomerCommand = {
            kind: 'create-customer', key: CREATE_CUSTOMER_KEY, requestId, payload: built.value,
            display: { name: built.value.name }, attempts: 0, lastError: null, frozenAt: new Date().toISOString(),
        };
        setPending(command);
        return command;
    };

    const submit = async () => {
        if (inFlight.current) return;
        const command = pending ?? freeze(false);
        if (command !== null) await send(command);
    };

    const createAnyway = async () => {
        if (inFlight.current || pending !== null) return;
        setSimilar(null);
        const command = freeze(true);
        if (command !== null) await send(command);
    };

    const e = (key: string) => fieldErrors[key];
    const field = (key: keyof CustomerDraft, label: string, required = false) => (
        <div>
            <label htmlFor={`customer-${key}-input`} className="text-xs font-medium text-gray-700">{label}{required ? ' *' : ''}</label>
            <input {...fieldProps(`customer-${key}`, e(key))} value={draft[key]} disabled={locked} maxLength={MAX[key]}
                onChange={(event) => edit(key, event.target.value)} className={inputClass} />
            <FieldError id={`customer-${key}`} messages={e(key)} />
        </div>
    );

    // Geen eigen <form>: het paneel staat binnen het orderformulier en een geneste form wordt door browsers niet verstuurd.
    // Enter in een tekstveld van dit paneel slaat de klant op en bereikt het orderformulier niet. Knoppen houden hun
    // gewone toetsenbordactivatie (Enter = klik), dus die worden hier niet onderschept.
    const onKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
        if (event.key !== 'Enter' || !(event.target instanceof HTMLInputElement)) return;
        event.preventDefault();
        event.stopPropagation();
        if (!locked || pending !== null) void submit();
    };

    return (
        <section data-testid="new-customer-panel" aria-busy={submitting} aria-labelledby="new-customer-title" onKeyDown={onKeyDown}
            className="space-y-3 rounded-md border border-blue-300 bg-blue-50/40 p-3">
            <div className="flex items-start justify-between gap-3">
                <div>
                    <h3 id="new-customer-title" className="text-sm font-semibold text-gray-900">{t('sales_new_customer_title')}</h3>
                    <p className="text-xs text-gray-600">{t('sales_new_customer_hint')}</p>
                </div>
                <button type="button" className={secondaryButtonClass} onClick={onClose} disabled={submitting}>{t('sales_close_form')}</button>
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="button" className={primaryButtonClass} onClick={() => void submit()}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {pending !== null && <p className="text-xs text-amber-950">{t('sales_customer_pending_replay_only')}</p>}
            {error && pending === null && <ErrorNotice error={error} />}

            {similar !== null && (
                <div role="alert" className="rounded border border-amber-400 bg-amber-50 px-3 py-2 text-sm text-amber-950">
                    <p className="font-medium">{t('sales_customer_similar_title')}</p>
                    <p className="mt-1 text-xs">{t('sales_customer_similar_hint')}</p>
                    <ul className="mt-2 space-y-1">
                        {similar.map((candidate) => (
                            <li key={candidate.id} className="flex flex-wrap items-center gap-2">
                                <span className="font-medium">{candidate.name}</span>
                                {(candidate.contactPerson || candidate.phone) && <span className="text-xs">{[candidate.contactPerson, candidate.phone].filter(Boolean).join(' · ')}</span>}
                                <button type="button" className={secondaryButtonClass} onClick={() => onChooseExisting(candidate.id)}>{t('sales_customer_use_existing')}</button>
                            </li>
                        ))}
                    </ul>
                    <button type="button" className={`${secondaryButtonClass} mt-2 border-amber-400`} onClick={() => void createAnyway()}>{t('sales_customer_create_anyway')}</button>
                </div>
            )}

            <div className="grid grid-cols-2 gap-3">
                <div className="col-span-2">{field('name', t('sales_customer_name'), true)}</div>
                {field('contactPerson', t('sales_customer_contact'))}
                {field('phone', t('sales_customer_phone'))}
                {field('email', t('sales_customer_email'))}
                {field('shippingAddress', t('sales_customer_shipping_address'))}
            </div>
            {liveSimilar.length > 0 && similar === null && pending === null && (
                <p role="status" className="text-xs text-amber-950">{t('sales_customer_similar_live', { names: liveSimilar.map((c) => c.name).join(', ') })}</p>
            )}

            {pending === null && (
                <div className="flex items-center gap-3">
                    <button type="button" className={primaryButtonClass} disabled={locked} onClick={() => void submit()}>{submitting ? t('sales_customer_saving') : t('sales_customer_save')}</button>
                </div>
            )}
        </section>
    );
}
