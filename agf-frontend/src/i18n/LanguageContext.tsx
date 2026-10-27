'use client';

import React, { createContext, useContext, useState, useEffect } from 'react';
import en from './en.json';
import nl from './nl.json';
import { reformatDecimalFieldsOnBlur, setInputLocale } from '../lib/decimal';

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

const STORAGE_KEY = 'agf_locale';

function isLocale(value: unknown): value is Locale {
    return value === 'en' || value === 'nl';
}

// Taalkeuze bij de eerste start: een eerder opgeslagen keuze wint altijd; anders de eerste browsertaal die Nederlands
// of Engels is; anders Nederlands. Pure functie zodat de volgorde testbaar is.
export function initialLocale(stored: string | null, browserLanguages: readonly string[]): Locale {
    if (isLocale(stored)) return stored;
    for (const language of browserLanguages) {
        const base = language.toLowerCase().split(/[-_]/)[0];
        if (base === 'nl') return 'nl';
        if (base === 'en') return 'en';
    }
    return 'nl';
}

function browserLanguages(): readonly string[] {
    if (typeof navigator === 'undefined') return [];
    if (Array.isArray(navigator.languages) && navigator.languages.length > 0) return navigator.languages;
    return navigator.language ? [navigator.language] : [];
}

function readStoredLocale(): string | null {
    try {
        return localStorage.getItem(STORAGE_KEY);
    } catch {
        return null;
    }
}

export const LanguageProvider = ({ children }: { children: React.ReactNode }) => {
    const [locale, setLocaleState] = useState<Locale>('nl');
    const [isClient, setIsClient] = useState(false);

    useEffect(() => {
        setLocaleState(initialLocale(readStoredLocale(), browserLanguages()));
        setIsClient(true);
    }, []);

    const setLocale = (newLocale: Locale) => {
        setLocaleState(newLocale);
        try {
            localStorage.setItem(STORAGE_KEY, newLocale);
        } catch {
            // Opslag niet beschikbaar: de keuze geldt dan alleen voor deze pagina.
        }
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

    // Getalvelden: invoer lezen in de taal van het scherm (vóór de kinderen renderen) en na blur in die notatie tonen (15000 -> 15.000).
    useEffect(() => reformatDecimalFieldsOnBlur(document), []);
    // Before hydration, we avoid rendering translations that might mismatch the server if we had SSR,
    // but since this is an internal ERP, we just render right away.
    if (!isClient) return null;
    setInputLocale(locale);

    return (
        <LanguageContext.Provider value={{ locale, setLocale, t }}>
            {children}
        </LanguageContext.Provider>
    );
};

export const useTranslation = () => useContext(LanguageContext);
