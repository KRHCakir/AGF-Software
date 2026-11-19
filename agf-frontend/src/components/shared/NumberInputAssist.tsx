'use client';

import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from '../../i18n/LanguageContext';
import { decimalField, decimalShapeProblem, setFieldValue } from '../../lib/decimal';

// De melding bij een getalveld met een ongeldige vorm (2.272.5, 15,000.5), voor alle getalvelden (inputMode "decimal") in één keer:
// "Ongeldig getal. Bedoel je 2.272,5?" met de knop Overnemen (of Alt+Enter in het veld), die de aanbevolen waarde invult via de
// eigen onChange van het veld. Opslaan blijft geblokkeerd door de controle van het formulier zelf: die leest met dezelfde parser.
// Eén keer gemonteerd in de LanguageProvider, naast de regels voor typen en verlaten (reformatDecimalFieldsOnBlur).
interface Hint { input: HTMLInputElement; suggestion: string | null; top: number; left: number }

const sameHints = (a: Hint[], b: Hint[]) => a.length === b.length && a.every((hint, i) =>
    hint.input === b[i].input && hint.suggestion === b[i].suggestion && hint.top === b[i].top && hint.left === b[i].left);

export default function NumberInputAssist() {
    const { t } = useTranslation();
    const [hints, setHints] = useState<Hint[]>([]);

    useEffect(() => {
        const tracked = new Set<HTMLInputElement>();
        let timer: ReturnType<typeof setInterval> | null = null;
        const measure = () => {
            const next: Hint[] = [];
            for (const input of tracked) {
                const problem = input.isConnected && !input.disabled && !input.readOnly ? decimalShapeProblem(input.value) : null;
                if (problem === null) {
                    tracked.delete(input);
                    input.removeAttribute('data-number-invalid');
                    continue;
                }
                input.setAttribute('data-number-invalid', 'true');
                const rect = input.getBoundingClientRect();
                next.push({ input, suggestion: problem.suggestion, top: Math.round(rect.bottom + 4), left: Math.round(Math.max(8, Math.min(rect.left, window.innerWidth - 296))) });
            }
            setHints((current) => (sameHints(current, next) ? current : next));
            // Zolang er een melding staat: de plek volgen (scrollen in een paneel, een formulier dat leeg wordt gemaakt).
            if (tracked.size > 0 && timer === null) timer = setInterval(measure, 400);
            if (tracked.size === 0 && timer !== null) { clearInterval(timer); timer = null; }
        };
        const onField = (event: Event) => {
            const input = decimalField(event.target, window);
            if (input) tracked.add(input);
            measure();
        };
        const onKey = (event: KeyboardEvent) => {
            if (event.key !== 'Enter' || !event.altKey) return;
            const input = decimalField(event.target, window);
            const suggestion = input && tracked.has(input) ? decimalShapeProblem(input.value)?.suggestion : null;
            if (!input || !suggestion) return;
            event.preventDefault();
            event.stopPropagation();
            setFieldValue(input, suggestion);
        };
        document.addEventListener('input', onField);
        document.addEventListener('focusout', onField);
        document.addEventListener('keydown', onKey, true);
        document.addEventListener('scroll', measure, true);
        window.addEventListener('resize', measure);
        return () => {
            document.removeEventListener('input', onField);
            document.removeEventListener('focusout', onField);
            document.removeEventListener('keydown', onKey, true);
            document.removeEventListener('scroll', measure, true);
            window.removeEventListener('resize', measure);
            if (timer !== null) clearInterval(timer);
        };
    }, []);

    if (hints.length === 0) return null;
    return createPortal(hints.map((hint, index) => (
        <div key={index} role="alert" data-testid="number-invalid" style={{ top: hint.top, left: hint.left }}
            className="fixed z-50 flex max-w-[18rem] flex-wrap items-center gap-x-2 gap-y-1 rounded border border-amber-400 bg-amber-50 px-2 py-1 text-xs text-amber-950 shadow">
            <span>{hint.suggestion === null ? t('number_invalid') : t('number_invalid_suggestion', { value: hint.suggestion })}</span>
            {hint.suggestion !== null && (
                <button type="button" data-testid="number-take-over" title={`${t('number_take_over')} (Alt+Enter)`}
                    className="rounded border border-amber-500 bg-white px-2 py-0.5 font-medium text-amber-950 hover:bg-amber-100"
                    // Het veld houdt de focus: Overnemen is geen verlaten van het veld.
                    onMouseDown={(event) => event.preventDefault()}
                    onClick={() => { setFieldValue(hint.input, hint.suggestion!); hint.input.focus(); }}>
                    {t('number_take_over')}
                </button>
            )}
        </div>
    )), document.body);
}
