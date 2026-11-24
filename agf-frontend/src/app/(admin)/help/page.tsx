'use client';

import { useTranslation } from '../../../i18n/LanguageContext';
import PageTitle from '../../../components/shared/PageTitle';

export default function HelpPage() {
    const { t } = useTranslation();
    return (
        <div className="p-8">
            <PageTitle className="mb-4">{t('help_title')}</PageTitle>
            <p className="max-w-2xl text-sm text-gray-800">{t('help_text')}</p>
        </div>
    );
}
