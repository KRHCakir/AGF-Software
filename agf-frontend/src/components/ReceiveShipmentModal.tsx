'use client';

import { useState, useEffect } from 'react';
import { InventoryService, Shipment } from '../services/inventoryService';
import { ProductService, Product } from '../services/productService';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
}

export default function ReceiveShipmentModal({ isOpen, onClose, onSuccess }: Props) {
    const [shipments, setShipments] = useState<Shipment[]>([]);
    const [products, setProducts] = useState<Product[]>([]);
    
    const [selectedShipmentId, setSelectedShipmentId] = useState('');
    const [selectedVariantId, setSelectedVariantId] = useState('');
    const [quantity, setQuantity] = useState('');
    const [weightKg, setWeightKg] = useState('');
    
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        if (isOpen) {
            InventoryService.getShipments().then(setShipments).catch(console.error);
            ProductService.getAll().then(setProducts).catch(console.error);
        }
    }, [isOpen]);

    if (!isOpen) return null;

    const allVariants = products.flatMap(p => p.variants.map(v => ({
        id: v.id,
        name: `${p.name} ${v.name}`
    })));

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);
        try {
            const shipId = parseInt(selectedShipmentId);
            const varId = parseInt(selectedVariantId);
            const qty = parseInt(quantity);
            const wt = weightKg ? parseFloat(weightKg) : undefined;
            
            await InventoryService.receiveShipment(shipId, varId, qty, wt);
            onSuccess();
        } catch (error: unknown) {
            console.error(error);
            alert(`Failed: ${error instanceof Error ? error.message : undefined}`);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white p-6 rounded-lg shadow-xl w-full max-w-md">
                <h2 className="text-xl font-bold mb-4">Receive Shipment (Ghost Sweeper)</h2>
                <form onSubmit={handleSubmit} className="space-y-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700">Target Shipment</label>
                        <select required value={selectedShipmentId} onChange={e => setSelectedShipmentId(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm p-2 border">
                            <option value="" disabled>Select a container...</option>
                            {shipments.map(s => (
                                <option key={s.id} value={s.id}>{s.containerCode} ({s.origin}) - {s.status}</option>
                            ))}
                        </select>
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700">Product Variant</label>
                        <select required value={selectedVariantId} onChange={e => setSelectedVariantId(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm p-2 border">
                            <option value="" disabled>Select product...</option>
                            {allVariants.map(v => (
                                <option key={v.id} value={v.id}>{v.name}</option>
                            ))}
                        </select>
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700">Quantity Physically Received (Units/Boxes)</label>
                        <input type="number" required min="1" value={quantity} onChange={e => setQuantity(e.target.value)} className="mt-1 block w-full rounded-md border-gray-300 shadow-sm p-2 border focus:ring-blue-500 focus:border-blue-500" />
                        <p className="text-xs text-gray-400 mt-1">If &quot;Backorder Ghosts&quot; exist for this variant, the system will dynamically absorb the debt from this quantity before adding the remainder to live stock.</p>
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700">Total Batch Weight (Kg) - Optional</label>
                        <input type="number" step="0.01" min="0" value={weightKg} onChange={e => setWeightKg(e.target.value)} placeholder="e.g. 950.5" className="mt-1 block w-full rounded-md border-gray-300 shadow-sm p-2 border focus:ring-blue-500 focus:border-blue-500 bg-gray-50" />
                    </div>

                    <div className="flex justify-end space-x-3 pt-4 border-t">
                        <button type="button" onClick={onClose} className="px-4 py-2 bg-gray-200 text-gray-800 rounded">Cancel</button>
                        <button type="submit" disabled={loading} className="px-4 py-2 bg-blue-600 text-white rounded font-bold">{loading ? 'Processing...' : 'Intake & Sweep Ghosts'}</button>
                    </div>
                </form>
            </div>
        </div>
    );
}
