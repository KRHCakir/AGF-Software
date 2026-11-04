'use client';

import { useEffect, useRef } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import type { FieldErrors } from '../supply/messages';

// Naast de opslaan-knop: als opslaan niet doorging, staat hier waarom (nooit alleen een melding ergens bovenaan buiten beeld), en de
// focus springt naar het eerste veld met een fout. Zonder zo'n veld (bijvoorbeeld een fout over de hele order) krijgt deze melding
// zelf de focus. Formulieren die zelf al naar het eerste foutveld springen, zetten `moveFocus` uit.
export default function SubmitErrors({ errors, moveFocus = true, testId = 'submit-errors' }: { errors: FieldErrors; moveFocus?: boolean; testId?: string }) {
    const { t } = useTranslation();
    const ref = useRef<HTMLParagraphElement>(null);
    const count = Object.values(errors).filter((messages) => messages !== undefined && messages.length > 0).length;

    useEffect(() => {
        if (count === 0 || !moveFocus) return;
        const form = ref.current?.closest('form');
        const first = form?.querySelector<HTMLElement>('[aria-invalid="true"]:not([disabled])');
        const target = first ?? ref.current;
        target?.focus();
        target?.scrollIntoView?.({ block: 'center' });
    }, [errors, count, moveFocus]);

    if (count === 0) return null;
    return (
        <p ref={ref} tabIndex={-1} role="alert" data-testid={testId} className="text-sm font-medium text-red-800 focus:outline-none">
            {t(count === 1 ? 'form_errors_next_to_save_one' : 'form_errors_next_to_save', { count: String(count) })}
        </p>
    );
}
