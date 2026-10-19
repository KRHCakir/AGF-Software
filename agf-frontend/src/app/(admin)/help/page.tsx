'use client';

import { useTranslation } from '../../../i18n/LanguageContext';

export default function HelpPage() {
    const { t } = useTranslation();
    return (
        <div className="p-8">
            <h1 className="text-2xl font-bold mb-4">{t('help_title')}</h1>
            <p className="max-w-2xl text-sm text-gray-800">{t('help_text')}</p>
        </div>
    );
}
