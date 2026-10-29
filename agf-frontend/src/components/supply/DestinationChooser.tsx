'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { SupplyRoute } from '../../types/availability';

// Expliciete keuze van de bestemming; er is bewust geen standaardwaarde.
export default function DestinationChooser({ value, onChange }: { value: SupplyRoute | null; onChange: (route: SupplyRoute) => void }) {
    const { t } = useTranslation();
    const options: { route: SupplyRoute; title: string; description: string; className: string }[] = [
        { route: 'Warehouse', title: t('supply_destination_warehouse'), description: t('supply_destination_warehouse_hint'), className: 'border-blue-300 has-[:checked]:bg-blue-50 has-[:checked]:ring-2 has-[:checked]:ring-blue-500' },
        { route: 'DirectTrade', title: t('supply_destination_direct'), description: t('supply_destination_direct_hint'), className: 'border-violet-300 has-[:checked]:bg-violet-50 has-[:checked]:ring-2 has-[:checked]:ring-violet-500' },
    ];
    return (
        <fieldset>
            <legend className="mb-2 text-sm font-semibold text-gray-900">{t('supply_destination_label')}</legend>
            <div className="grid grid-cols-2 gap-3">
                {options.map((option) => (
                    <label key={option.route} className={`flex cursor-pointer gap-3 rounded-md border bg-white p-3 ${option.className}`}>
                        <input type="radio" name="supply-destination" value={option.route} checked={value === option.route} data-tour="supply-destination" data-tour-key={option.route}
                            onChange={() => onChange(option.route)} className="mt-1" />
                        <span>
                            <span className="block font-semibold text-gray-900">
                                {option.route === 'DirectTrade' && <span aria-hidden="true">🔒 </span>}
                                {option.title}
                            </span>
                            <span className="block text-sm text-gray-700">{option.description}</span>
                        </span>
                    </label>
                ))}
            </div>
        </fieldset>
    );
}
