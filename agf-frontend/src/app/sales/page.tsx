'use client';

import Link from 'next/link';

export default function SalesSelectionPage() {
    // Mock Sellers for MVP
    const sellers = [
        { id: 1, name: 'Jan de Vries', role: 'Sales Manager', avatar: 'JD' },
        { id: 2, name: 'Sarah Bakker', role: 'Senior Seller', avatar: 'SB' },
        { id: 3, name: 'Klaas Jansen', role: 'Junior Seller', avatar: 'KJ' },
    ];

    return (
        <div className="min-h-screen bg-gray-50 flex flex-col items-center justify-center p-4">
            <h1 className="text-3xl font-bold text-gray-800 mb-8">Select Seller</h1>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-6 w-full max-w-4xl">
                {sellers.map(seller => (
                    <Link key={seller.id} href={`/sales/${seller.id}`} className="block">
                        <div className="bg-white hover:bg-blue-50 border border-gray-200 hover:border-blue-300 rounded-xl p-8 flex flex-col items-center text-center shadow-sm transition-all cursor-pointer h-full">
                            <div className="w-20 h-20 bg-blue-600 rounded-full flex items-center justify-center text-white text-2xl font-bold mb-4">
                                {seller.avatar}
                            </div>
                            <h2 className="text-xl font-bold text-gray-800">{seller.name}</h2>
                            <p className="text-gray-500">{seller.role}</p>

                            <div className="mt-6 w-full">
                                <span className="inline-block w-full py-2 bg-blue-600 text-white rounded-lg font-medium">
                                    Start Selling
                                </span>
                            </div>
                        </div>
                    </Link>
                ))}
            </div>

            <div className="mt-12 text-center text-gray-400 text-sm">
                <p>AGF Fast Sales Interface • v0.1</p>
                <Link href="/dashboard" className="hover:underline">Back to Admin Dashboard</Link>
            </div>
        </div>
    );
}
