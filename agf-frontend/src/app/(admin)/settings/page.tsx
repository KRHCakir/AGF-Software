'use client';

import AiReadingSetting from '../../../components/settings/AiReadingSetting';
import CostArticlesAdmin from '../../../components/settings/CostArticlesAdmin';
import GlobalGapPortalSetting from '../../../components/settings/GlobalGapPortalSetting';
import PartijNumberingSetting from '../../../components/settings/PartijNumberingSetting';
import PaymentConditionsAdmin from '../../../components/settings/PaymentConditionsAdmin';
import { useTranslation } from '../../../i18n/LanguageContext';

// Instellingen: bedrijfsbrede keuzelijsten en adressen (één bedrijf, geen tenantstructuur). Betalingscondities worden door
// klant- en leveranciersrollen gedeeld; het GLOBALG.A.P.-portaaladres gebruikt de handmatige GGN-controle op de leverancierskaart; de
// partijnummering bepaalt het voorgestelde partijnummer bij een nieuwe inkooporder; de kostenartikelen (type Dienst) staan in de
// artikelkeuze van een inkoopfactuurregel; AI-lezen van inkoopfacturen staat standaard uit (aanzetten met bevestiging, maandlimiet).
export default function SettingsPage() {
    const { t } = useTranslation();
    return (
        <div className="mx-auto max-w-5xl space-y-3 p-2">
            <div>
                <h1 className="text-2xl font-bold text-gray-900">{t('settings_title')}</h1>
                <p className="text-sm text-gray-600">{t('settings_subtitle')}</p>
            </div>
            <PartijNumberingSetting />
            <PaymentConditionsAdmin />
            <CostArticlesAdmin />
            <GlobalGapPortalSetting />
            <AiReadingSetting />
        </div>
    );
}
