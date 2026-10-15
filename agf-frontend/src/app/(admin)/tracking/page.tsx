'use client';

import { useTranslation } from '../../../i18n/LanguageContext';

export default function TrackingPage() {
    const { t } = useTranslation();
    return (
        <div className="p-4">
            <h1 className="text-2xl font-bold mb-4">{t('shipment_tracking')}</h1>
            <div className="bg-white p-8 rounded shadow text-center text-gray-500 h-96 flex flex-col justify-center items-center bg-blue-50">
                <span className="text-4xl block mb-2">🌍</span>
                <p>{t('live_map_integration')}</p>
                <p className="text-xs mt-2">{t('marine_traffic_api')}</p>
            </div>
        </div>
    );
}
