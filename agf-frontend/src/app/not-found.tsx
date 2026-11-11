'use client';

import Link from 'next/link';
import { LanguageProvider, useTranslation } from '../i18n/LanguageContext';

// Een adres dat niet (meer) bestaat, bijvoorbeeld een bewaarde link naar een oud scherm dat met "Oud" uit het menu verdween
// (feedback 11 november): een nette melding met een knop naar Start, in plaats van de standaard foutpagina.
function NotFoundMessage() {
    const { t } = useTranslation();
    return (
        <main className="mx-auto flex min-h-dvh max-w-xl flex-col items-center justify-center gap-4 p-6 text-center" data-testid="not-found">
            <h1 className="text-2xl font-bold text-gray-900">{t('not_found_title')}</h1>
            <p className="text-base text-gray-700">{t('not_found_text')}</p>
            <Link href="/dashboard" className="flex min-h-11 items-center rounded-md bg-blue-700 px-5 text-base font-semibold text-white hover:bg-blue-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2">
                {t('not_found_go_start')}
            </Link>
        </main>
    );
}

export default function NotFound() {
    return (
        <LanguageProvider>
            <NotFoundMessage />
        </LanguageProvider>
    );
}
