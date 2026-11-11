'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { orderNumbers } from '../../lib/purchaseOrderRules';

// Het nummer van een inkooporder zoals de handelaar het kent: het ordernummer van de leverancier als hoofdtekst, met het eigen IO-nummer
// klein en grijs als "Intern nummer"; zonder leveranciersnummer alleen het IO-nummer (zonder intern-label). stacked: het interne nummer
// op een eigen regel (lijst, detail); anders erachter (in een zin). truncate: een lang nummer afgekapt met … (volledig in de title).
export default function OrderNumberText({ id, orderNumber, supplierReference, stacked = false, truncate = false, mainClassName = '' }: {
    id?: number;
    orderNumber?: string | null;
    supplierReference?: string | null;
    stacked?: boolean;
    truncate?: boolean;
    mainClassName?: string;
}) {
    const { t } = useTranslation();
    const { main, internal } = orderNumbers({ id, orderNumber, supplierReference });
    const fit = truncate ? 'block max-w-[14rem] truncate max-md:max-w-full' : '[overflow-wrap:anywhere]';
    return (
        <span className={stacked ? 'inline-block min-w-0 max-w-full align-top' : 'min-w-0'}>
            <span className={`font-mono ${fit} ${mainClassName}`} title={truncate ? main : undefined} data-testid="order-number-main">{main}</span>
            {internal && (
                <span className={`${stacked ? 'block' : 'ml-1'} whitespace-nowrap font-sans text-xs font-normal text-gray-600`} data-testid="order-number-internal">
                    {t('po_internal_number')} <span className="font-mono">{internal}</span>
                </span>
            )}
        </span>
    );
}
