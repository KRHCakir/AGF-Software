'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { PendingCommand, PendingCommands, sortedCommands } from '../../lib/pendingCommands';
import { secondaryButtonClass } from '../supply/SuccessPanel';

export function pendingLabel(command: PendingCommand, t: (key: string, variables?: Record<string, string>) => string): string {
    switch (command.kind) {
        case 'create-order': return t('sales_pending_create', { customer: command.display.customerName });
        case 'reserve': return t('sales_pending_reserve', { pallet: command.display.palletReference, lot: command.display.lotNumber, id: String(command.orderId), product: command.display.productLabel });
        case 'release': return t('sales_pending_release', { pallet: command.display.palletReference, lot: command.display.lotNumber, id: String(command.orderId), product: command.display.productLabel });
        case 'cancel-order': return t('sales_pending_cancel', { id: String(command.orderId), customer: command.display.customerName, count: String(command.display.activeReservationCount) });
        case 'dispatch': return t('sales_pending_dispatch', { id: String(command.orderId), customer: command.display.customerName, count: String(command.display.lineCount) });
        default: return t('sales_pending_correct', { id: String(command.orderId), dispatch: String(command.dispatchId), customer: command.display.customerName, count: String(command.display.lineCount) });
    }
}

// Overzicht van commando's met een onbekende uitkomst. Ze worden nooit weggegooid: alleen een herhaling met dezelfde
// requestId (of een definitief antwoord van de server) rondt ze af.
export default function PendingCommandsBanner({ commands, onGoTo }: { commands: PendingCommands; onGoTo: (command: PendingCommand) => void }) {
    const { t } = useTranslation();
    const list = sortedCommands(commands);
    if (list.length === 0) return null;
    return (
        <section role="alert" aria-labelledby="pending-commands-title" className="rounded-md border border-amber-400 bg-amber-50 px-4 py-3 text-sm text-amber-950">
            <h2 id="pending-commands-title" className="font-semibold">{t('sales_pending_title', { count: String(list.length) })}</h2>
            <p className="mt-1 text-xs">{t('sales_pending_hint')}</p>
            <ul className="mt-2 space-y-1">
                {list.map((command) => (
                    <li key={command.key} className="flex flex-wrap items-center gap-2">
                        <span className="font-medium">{pendingLabel(command, t)}</span>
                        <span className="text-xs">{t('sales_pending_attempts', { n: String(command.attempts), error: command.lastError ?? '–' })}</span>
                        <button type="button" className={secondaryButtonClass} onClick={() => onGoTo(command)}>{t('sales_pending_go_to')}</button>
                    </li>
                ))}
            </ul>
        </section>
    );
}

// Melding in een formulier waarvan het commando nog openstaat: alleen exact dezelfde herhaling is mogelijk.
export function PendingNotice({ command, submitting, children }: { command: PendingCommand; submitting: boolean; children?: React.ReactNode }) {
    const { t } = useTranslation();
    return (
        <div role="alert" className="rounded-md border border-amber-400 bg-amber-50 px-3 py-2 text-sm text-amber-950">
            <p className="font-medium">{t('supply_error_outcome_unknown')}</p>
            <p className="mt-1 text-xs">{t('sales_pending_kept')} {t('sales_pending_attempts', { n: String(command.attempts), error: command.lastError ?? '–' })}</p>
            {submitting ? <p className="mt-2 text-xs">{t('supply_saving')}</p> : children && <div className="mt-2 flex flex-wrap gap-2">{children}</div>}
        </div>
    );
}
