'use client';

import React, { createContext, useContext, useState, useEffect } from 'react';
import en from './en.json';
import nl from './nl.json';

type Locale = 'en' | 'nl';
type Dictionaries = { [key in Locale]: Record<string, string> };

const dictionaries: Dictionaries = { en, nl };

interface LanguageContextProps {
    locale: Locale;
    setLocale: (locale: Locale) => void;
    t: (key: string, variables?: Record<string, string>) => string;
}

const defaultContext: LanguageContextProps = {
    locale: 'en',
    setLocale: () => { },
    t: (key) => key,
};

const LanguageContext = createContext<LanguageContextProps>(defaultContext);

export const LanguageProvider = ({ children }: { children: React.ReactNode }) => {
    const [locale, setLocaleState] = useState<Locale>('en'); // Default to EN
    const [isClient, setIsClient] = useState(false);

    useEffect(() => {
        setIsClient(true);
        const stored = localStorage.getItem('agf_locale') as Locale;
        if (stored && (stored === 'en' || stored === 'nl')) {
            setLocaleState(stored);
        }
    }, []);

    const setLocale = (newLocale: Locale) => {
        setLocaleState(newLocale);
        localStorage.setItem('agf_locale', newLocale);
    };

    const t = (key: string, variables?: Record<string, string>) => {
        const dict = dictionaries[locale];
        let text = dict[key] || key;

        if (variables) {
            Object.keys(variables).forEach((varKey) => {
                const varValue = variables[varKey];
                text = text.replace(new RegExp(`{${varKey}}`, 'g'), varValue);
            });
        }

        return text;
    };

    // Before hydration, we avoid rendering translations that might mismatch the server if we had SSR,
    // but since this is an internal ERP, we just render right away.
    if (!isClient) return null;

    return (
        <LanguageContext.Provider value={{ locale, setLocale, t }}>
            {children}
        </LanguageContext.Provider>
    );
};

export const useTranslation = () => useContext(LanguageContext);
