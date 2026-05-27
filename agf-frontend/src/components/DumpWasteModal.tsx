'use client';

import { useState } from 'react';
import { InventoryService } from '../services/inventoryService';

interface Props {
    isOpen: boolean;
    inventoryId: number;
    currentQuantity: number;
    onClose: () => void;
    onSuccess: () => void;
}

export default function DumpWasteModal({ isOpen, inventoryId, currentQuantity, onClose, onSuccess }: Props) {
    const [quantity, setQuantity] = useState('');
    const [weightKg, setWeightKg] = useState('');
    const [reason, setReason] = useState('Rotten/Spoiled');
    const [loading, setLoading] = useState(false);

    if (!isOpen) return null;

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);
        try {
            const qty = parseInt(quantity);
            const wgt = weightKg ? parseFloat(weightKg) : undefined;
            if (qty > currentQuantity) {
                alert(`Cannot dump more than the available ${currentQuantity} units.`);
                setLoading(false);
                return;
            }
            await InventoryService.registerWaste(inventoryId, qty, wgt, reason);
            onSuccess();
        } catch (error: any) {
            console.error(error);
            alert(`Failed to log waste: ${error.message}`);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="fixed inset-0 bg-black bg-opacity-60 flex items-center justify-center z-50">
            <div className="bg-white p-6 rounded-lg shadow-xl w-full max-w-sm border-t-4 border-red-500">
                <div className="flex items-center mb-4 text-red-600">
                    <span className="text-3xl mr-3">🗑️</span>
                    <h2 className="text-xl font-black">Register Waste</h2>
                </div>
                
                <p className="text-sm text-gray-600 mb-4">
                    This unit deduction will be permanently logged in the 10-year audit ledger as shrinkage.
                </p>

                <form onSubmit={handleSubmit} className="space-y-4">
                    <div>
                        <label className="block text-sm font-bold text-gray-700">Quantity to Dump (Must be &le; {currentQuantity})</label>
                        <input type="number" required min="1" max={currentQuantity} value={quantity} onChange={e => setQuantity(e.target.value)} className="mt-1 block w-full rounded border-gray-300 shadow-sm p-2 border focus:ring-red-500 focus:border-red-500 bg-red-50 text-red-900 font-bold" />
                    </div>

                    <div>
                        <label className="block text-sm font-bold text-gray-700">Estimated Weight (Kg) - Optional</label>
                        <input type="number" step="0.01" min="0" value={weightKg} onChange={e => setWeightKg(e.target.value)} className="mt-1 block w-full rounded border-gray-300 shadow-sm p-2 border focus:ring-red-500 focus:border-red-500 bg-gray-50 text-gray-900" />
                    </div>

                    <div>
                        <label className="block text-sm font-bold text-gray-700">Reason</label>
                        <select required value={reason} onChange={e => setReason(e.target.value)} className="mt-1 block w-full rounded border-gray-300 shadow-sm p-2 border focus:ring-red-500 focus:border-red-500">
                            <optgroup label="Physical QC Spoilage">
                                <option value="Rotten/Spoiled">Rotten/Spoiled</option>
                                <option value="Damaged Crate">Damaged Crate</option>
                                <option value="Quality Rejection">Quality Rejection (QC)</option>
                            </optgroup>
                            <optgroup label="Administrative Adjustments">
                                <option value="Admin Correction">Admin Correction / Miscount</option>
                                <option value="Samples">Given as Samples</option>
                                <option value="Other">Other Admin Adjustment</option>
                            </optgroup>
                        </select>
                    </div>

                    <div className="flex justify-end space-x-3 pt-4 border-t">
                        <button type="button" onClick={onClose} className="px-4 py-2 bg-gray-200 text-gray-800 rounded font-bold hover:bg-gray-300">Cancel</button>
                        <button type="submit" disabled={loading} className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded font-bold shadow-md">{loading ? 'Processing...' : 'Confirm Waste'}</button>
                    </div>
                </form>
            </div>
        </div>
    );
}
