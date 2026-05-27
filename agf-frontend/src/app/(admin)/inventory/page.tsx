'use client';

import { useState, useEffect } from 'react';
import { InventoryService, InventoryItem } from '../../../services/inventoryService';
import ReceiveShipmentModal from '../../../components/ReceiveShipmentModal';
import DumpWasteModal from '../../../components/DumpWasteModal';
import { useTranslation } from '../../../i18n/LanguageContext';

export default function InventoryPage() {
    const { t } = useTranslation();
    const [inventory, setInventory] = useState<InventoryItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [dumpTargetId, setDumpTargetId] = useState<number | null>(null);
    const [dumpQuantity, setDumpQuantity] = useState<number>(0);

    const loadData = async () => {
        setLoading(true);
        try {
            const data = await InventoryService.getInventory();
            setInventory(data);
        } catch (error) {
            console.error("Error loading inventory", error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, []);

    // Grouping by Variant for the Tier 2 Breakdown View
    const grouped = inventory.reduce((acc, item) => {
        const key = item.productVariantName || 'Unknown';
        if (!acc[key]) {
            acc[key] = {
                totalQuantity: 0,
                batches: [] as typeof inventory
            };
        }
        acc[key].totalQuantity += item.quantity;
        acc[key].batches.push(item);
        return acc;
    }, {} as Record<string, { totalQuantity: number, batches: typeof inventory }>);

    return (
        <div className="p-6">
            <div className="flex justify-between items-center mb-6">
                <h1 className="text-2xl font-bold text-gray-800">{t('warehouse_inventory_batched')}</h1>
                <button 
                    onClick={() => setIsModalOpen(true)}
                    className="bg-purple-600 hover:bg-purple-700 text-white font-bold py-2 px-4 rounded-lg shadow transition-colors flex items-center"
                >
                    <span className="mr-2">📥</span> {t('intake_shipment')}
                </button>
            </div>

            {loading ? (
                <div className="text-gray-500">{t('loading_tier_2')}</div>
            ) : (
                <div className="space-y-6">
                    {Object.entries(grouped).map(([variantName, data]) => (
                        <div key={variantName} className="bg-white rounded-lg shadow-sm border border-gray-200 overflow-hidden">
                            <div className="bg-gray-50 px-6 py-4 flex justify-between items-center border-b">
                                <h3 className="text-lg font-bold text-gray-800">{variantName}</h3>
                                <div className={`text-lg font-bold px-3 py-1 rounded-full ${data.totalQuantity < 0 ? 'bg-red-100 text-red-800' : 'bg-green-100 text-green-800'}`}>
                                    {t('total_volume')} {data.totalQuantity} {data.totalQuantity < 0 && t('pre_sold_debt')}
                                </div>
                            </div>
                            <div className="p-0">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50 text-xs text-gray-500 uppercase tracking-wider">
                                        <tr>
                                            <th className="px-6 py-2 text-left">{t('location_batch_type')}</th>
                                            <th className="px-6 py-2 text-left">{t('source_shipment')}</th>
                                            <th className="px-6 py-2 text-left">{t('stored_date')}</th>
                                            <th className="px-6 py-2 text-right">{t('batch_quantity')}</th>
                                            <th className="px-6 py-2 text-right">{t('actions')}</th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-100">
                                        {data.batches.map(batch => (
                                            <tr key={batch.id} className={batch.quantity < 0 ? 'bg-red-50' : 'hover:bg-gray-50'}>
                                                <td className="px-6 py-3 whitespace-nowrap text-sm font-medium text-gray-900 border-l-4 border-transparent" style={{borderLeftColor: batch.quantity < 0 ? '#ef4444' : '#3b82f6'}}>
                                                    {batch.location}
                                                </td>
                                                <td className="px-6 py-3 whitespace-nowrap text-sm text-gray-500">
                                                    {batch.shipment ? (
                                                        <span className="bg-blue-100 text-blue-800 text-xs px-2 py-1 rounded border border-blue-200">
                                                            {batch.shipment.containerCode} ({batch.shipment.origin})
                                                        </span>
                                                    ) : (
                                                        <span className="text-gray-400 italic">{t('no_batch_generic')}</span>
                                                    )}
                                                </td>
                                                <td className="px-6 py-3 whitespace-nowrap text-sm text-gray-500">
                                                    {new Date(batch.stored).toLocaleDateString()}
                                                </td>
                                                <td className={`px-6 py-3 whitespace-nowrap text-sm text-right font-bold ${batch.quantity < 0 ? 'text-red-600' : 'text-gray-900'}`}>
                                                    {batch.quantity}
                                                </td>
                                                <td className="px-6 py-3 whitespace-nowrap text-right space-x-2">
                                                    {batch.quantity > 0 && (
                                                        <>
                                                            <button 
                                                                onClick={async () => {
                                                                    try {
                                                                        if (batch.isFrozen) {
                                                                            await InventoryService.unfreezeBatch(batch.id);
                                                                        } else {
                                                                            await InventoryService.freezeBatch(batch.id);
                                                                        }
                                                                        loadData();
                                                                    } catch(e) {
                                                                        console.error(e);
                                                                    }
                                                                }}
                                                                className={`px-2 py-1 rounded text-xs font-bold transition-colors shadow-sm ${batch.isFrozen ? 'bg-blue-200 text-blue-800' : 'bg-gray-200 text-gray-800 hover:bg-blue-100'}`}
                                                                title={batch.isFrozen ? t('unfreeze_batch') : t('freeze_batch')}
                                                            >
                                                                {batch.isFrozen ? `🧊 ${t('frozen')}` : `❄️ ${t('freeze')}`}
                                                            </button>
                                                            <button 
                                                                onClick={() => {
                                                                    setDumpTargetId(batch.id);
                                                                    setDumpQuantity(batch.quantity);
                                                                }}
                                                                className="text-white bg-red-500 hover:bg-red-600 px-2 py-1 rounded text-xs font-bold transition-colors shadow-sm"
                                                                title={t('register_waste')}
                                                            >
                                                                🗑️ {t('dump')}
                                                            </button>
                                                        </>
                                                    )}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    ))}
                    {Object.keys(grouped).length === 0 && (
                        <div className="text-center p-12 text-gray-500 bg-white rounded-lg shadow">
                            {t('no_inventory_records')}
                        </div>
                    )}
                </div>
            )}

            {isModalOpen && (
                <ReceiveShipmentModal 
                    isOpen={isModalOpen}
                    onClose={() => setIsModalOpen(false)}
                    onSuccess={() => {
                        alert("Ghost Sweeper executed safely! Inventory pools mapped.");
                        setIsModalOpen(false);
                        loadData();
                    }}
                />
            )}
            {dumpTargetId !== null && (
                <DumpWasteModal
                    isOpen={dumpTargetId !== null}
                    inventoryId={dumpTargetId}
                    currentQuantity={dumpQuantity}
                    onClose={() => setDumpTargetId(null)}
                    onSuccess={() => {
                        alert("Waste registered. 10-Year Audit Trail updated.");
                        setDumpTargetId(null);
                        loadData();
                    }}
                />
            )}
        </div>
    );
}
