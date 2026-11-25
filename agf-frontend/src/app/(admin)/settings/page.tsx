'use client';

import RevenueTypesSetting from '../../../components/settings/RevenueTypesSetting';
import AiReadingSetting from '../../../components/settings/AiReadingSetting';
import CostArticlesAdmin from '../../../components/settings/CostArticlesAdmin';
import GlobalGapPortalSetting from '../../../components/settings/GlobalGapPortalSetting';
import CertificatesSetting from '../../../components/settings/CertificatesSetting';
import PartijNumberingSetting from '../../../components/settings/PartijNumberingSetting';
import WeightDisplaySetting from '../../../components/settings/WeightDisplaySetting';
import PurchaseCurrenciesSetting from '../../../components/settings/PurchaseCurrenciesSetting';
import LoadingSlipSetting from '../../../components/settings/LoadingSlipSetting';
import TransportOrderSetting from '../../../components/settings/TransportOrderSetting';
import WarehouseRecipientsSetting from '../../../components/settings/WarehouseRecipientsSetting';
import MailSenderSetting from '../../../components/settings/MailSenderSetting';
import MailTemplatesSetting from '../../../components/settings/MailTemplatesSetting';
import PaymentConditionsAdmin from '../../../components/settings/PaymentConditionsAdmin';
import StockLocationsSetting from '../../../components/settings/StockLocationsSetting';
import { useTranslation } from '../../../i18n/LanguageContext';
import PageTitle from '../../../components/shared/PageTitle';

// Instellingen: bedrijfsbrede keuzelijsten en adressen (één bedrijf, geen tenantstructuur). Betalingscondities worden door
// klant- en leveranciersrollen gedeeld; het GLOBALG.A.P.-portaaladres gebruikt de handmatige GGN-controle op de leverancierskaart; de
// partijnummering bepaalt het voorgestelde partijnummer bij een nieuwe inkooporder; de kostenartikelen (type Dienst) staan in de
// artikelkeuze van een inkoopfactuurregel; AI-lezen van inkoopfacturen staat standaard uit (aanzetten met bevestiging, maandlimiet).
export default function SettingsPage() {
    const { t } = useTranslation();
    return (
        <div className="mx-auto max-w-5xl space-y-3 p-2">
            <div>
                <PageTitle>{t('settings_title')}</PageTitle>
                <p className="text-sm text-gray-600">{t('settings_subtitle')}</p>
            </div>
            <PartijNumberingSetting />
            <WeightDisplaySetting />
            <PurchaseCurrenciesSetting />
            <StockLocationsSetting />
            <LoadingSlipSetting />
            <TransportOrderSetting />
            <WarehouseRecipientsSetting />
            <MailSenderSetting />
            <MailTemplatesSetting />
            <RevenueTypesSetting />
            <PaymentConditionsAdmin />
            <CostArticlesAdmin />
            <GlobalGapPortalSetting />
            <CertificatesSetting />
            <AiReadingSetting />
        </div>
    );
}
