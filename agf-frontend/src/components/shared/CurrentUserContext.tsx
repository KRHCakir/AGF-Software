'use client';

import { createContext, ReactNode, useContext, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { getCurrentUser } from '../../services/currentUserService';
import type { CurrentUser } from '../../types/currentUser';

// Eén keer per sessie geladen; alle schermen lezen dezelfde gebruiker. null zolang hij laadt of als de API niet antwoordt
// (dan toont de kop niets; de server legt bij elke actie toch zelf de juiste gebruiker vast).
const CurrentUserContext = createContext<CurrentUser | null>(null);

export function CurrentUserProvider({ children }: { children: ReactNode }) {
    const [user, setUser] = useState<CurrentUser | null>(null);
    useEffect(() => {
        const controller = new AbortController();
        getCurrentUser(controller.signal).then(setUser).catch(() => { /* zonder naam blijft alles werken */ });
        return () => controller.abort();
    }, []);
    return <CurrentUserContext.Provider value={user}>{children}</CurrentUserContext.Provider>;
}

export function useCurrentUser(): CurrentUser | null {
    return useContext(CurrentUserContext);
}

// "Ingelogd als Jan": op de plek waar vroeger een medewerker getypt werd. Geen invoer.
export function SignedInAs({ className = '' }: { className?: string }) {
    const { t } = useTranslation();
    const user = useCurrentUser();
    if (user === null) return null;
    return (
        <p className={`text-sm text-gray-700 ${className}`} data-testid="signed-in-as" data-tour="signed-in-as">
            {t('signed_in_as', { name: user.displayName })}
        </p>
    );
}
