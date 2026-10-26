'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import type { RelationWarning } from '../../types/relation';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Waarschuwingen op de relatiekaart, vertaald op code (nooit op de servertekst). Staande waarschuwingen (onbevestigde
// standaardrekening, verzekering, inactieve conditie) blijven tot de oorzaak weg is; e-mailwaarschuwingen komen alleen uit het
// antwoord op opslaan. Een onbevestigde standaardrekening staat bovenaan en valt op: betalen naar een niet-bevestigde rekening
// is het risico waar deze controle voor bestaat. Een tikfoutsuggestie is met één klik over te nemen; niets hiervan blokkeert opslaan.

const KNOWN = new Set(['email-typo', 'email-no-mail-server', 'email-domain-unchecked', 'default-bank-account-unconfirmed', 'insurance-expired', 'insurance-expires-soon', 'insurance-gap', 'payment-condition-inactive']);

export default function RelationWarnings({ warnings, onApplySuggestion, fieldLabel }: {
    warnings: readonly RelationWarning[];
    onApplySuggestion: (field: string, suggestion: string) => void;
    fieldLabel: (field: string | null) => string;
}) {
    const { t } = useTranslation();
    if (warnings.length === 0) return null;
    const bank = warnings.filter((w) => w.code === 'default-bank-account-unconfirmed');
    const others = warnings.filter((w) => w.code !== 'default-bank-account-unconfirmed');
    const text = (w: RelationWarning) => (KNOWN.has(w.code)
        ? t(`relations_warning_${w.code.replace(/-/g, '_')}`, { field: fieldLabel(w.field), suggestion: w.suggestion ?? '', role: w.field?.startsWith('vendor') ? t('relations_role_vendor') : t('relations_role_customer') })
        : w.message);

    return (
        <div className="space-y-2" data-testid="relation-warnings">
            {bank.map((w, index) => (
                <div key={`bank-${index}`} role="alert" className="rounded border-2 border-red-400 bg-red-50 px-3 py-2 text-sm text-red-950" data-testid="warning-default-bank-account-unconfirmed">
                    <p className="font-semibold">⚠ {t('relations_warning_default_bank_account_unconfirmed')}</p>
                    <p className="mt-1 text-xs">{t('relations_warning_default_bank_account_unconfirmed_hint')}</p>
                </div>
            ))}
            {others.length > 0 && (
                <ul className="space-y-1 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-950">
                    {others.map((w, index) => (
                        <li key={`${w.code}-${index}`} className="flex flex-wrap items-center gap-2" data-testid={`warning-${w.code}`}>
                            <span>{text(w)}</span>
                            {w.code === 'email-typo' && w.suggestion && w.field && (
                                <button type="button" className={`${secondaryButtonClass} border-amber-400 px-2 py-0.5 text-xs`} onClick={() => onApplySuggestion(w.field!, w.suggestion!)}>
                                    {t('relations_email_apply', { suggestion: w.suggestion })}
                                </button>
                            )}
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}
