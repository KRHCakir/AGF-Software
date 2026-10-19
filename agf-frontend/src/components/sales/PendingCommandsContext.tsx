'use client';

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { PendingCommand, PendingCommands, parseStoredCommands, serializeCommands } from '../../lib/pendingCommands';

// Onafgeronde commando's boven de formulieren, per tabblad bewaard (sessionStorage). Een formulier dat opnieuw wordt
// opgebouwd (ander scherm, andere order, ander voorraadregel, verversen) krijgt exact dezelfde payload en requestId terug.
const STORAGE_KEY = 'agf_sales_pending_commands';

interface PendingCommandsContextValue {
    hydrated: boolean;
    commands: PendingCommands;
    setCommand: (command: PendingCommand) => void;
    clearCommand: (key: string) => void;
}

const PendingCommandsContext = createContext<PendingCommandsContextValue>({
    hydrated: false,
    commands: {},
    setCommand: () => undefined,
    clearCommand: () => undefined,
});

export function PendingCommandsProvider({ children }: { children: React.ReactNode }) {
    const [commands, setCommands] = useState<PendingCommands>({});
    const [hydrated, setHydrated] = useState(false);

    useEffect(() => {
        try {
            setCommands(parseStoredCommands(sessionStorage.getItem(STORAGE_KEY)));
        } catch {
            // Geen opslag beschikbaar: dan alleen in het geheugen van deze pagina.
        }
        setHydrated(true);
    }, []);

    useEffect(() => {
        if (!hydrated) return;
        try {
            if (Object.keys(commands).length === 0) sessionStorage.removeItem(STORAGE_KEY);
            else sessionStorage.setItem(STORAGE_KEY, serializeCommands(commands));
        } catch {
            // Niet kunnen bewaren is geen fout voor de lopende actie.
        }
    }, [commands, hydrated]);

    const setCommand = useCallback((command: PendingCommand) => {
        setCommands((current) => ({ ...current, [command.key]: command }));
    }, []);

    const clearCommand = useCallback((key: string) => {
        setCommands((current) => {
            if (!(key in current)) return current;
            const next = { ...current };
            delete next[key];
            return next;
        });
    }, []);

    const value = useMemo(() => ({ hydrated, commands, setCommand, clearCommand }), [hydrated, commands, setCommand, clearCommand]);
    return <PendingCommandsContext.Provider value={value}>{children}</PendingCommandsContext.Provider>;
}

export function usePendingCommands(): PendingCommandsContextValue {
    return useContext(PendingCommandsContext);
}

// Eén sleutel: het onafgeronde commando op die plaats, plus bewaren en opruimen.
export function usePendingCommand<T extends PendingCommand>(key: string): [T | null, (command: T) => void, () => void] {
    const { commands, setCommand, clearCommand } = usePendingCommands();
    const clear = useCallback(() => clearCommand(key), [clearCommand, key]);
    return [(commands[key] as T | undefined) ?? null, setCommand, clear];
}
