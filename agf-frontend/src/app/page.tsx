import Link from 'next/link';

export default function LandingPage() {
    return (
        <div className="min-h-screen bg-gradient-to-br from-blue-900 to-slate-800 flex flex-col items-center justify-center p-6 text-white">
            <div className="max-w-4xl w-full text-center mb-12">
                <h1 className="text-5xl font-extrabold mb-4 tracking-tight drop-shadow-md">
                    AGF<span className="text-blue-300">Hub</span>
                </h1>
                <p className="text-xl text-blue-100 max-w-2xl mx-auto">
                    Select your workspace mode to continue.
                </p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-8 w-full max-w-3xl">
                {/* DESKTOP / ADMIN CARD */}
                <Link href="/dashboard" className="group block">
                    <div className="bg-white/10 backdrop-blur-md border border-white/20 hover:bg-white/20 hover:border-white/40 rounded-2xl p-10 flex flex-col items-center text-center transition-all duration-300 transform hover:-translate-y-2 shadow-xl h-full">
                        <div className="w-24 h-24 bg-gradient-to-tr from-blue-500 to-cyan-400 rounded-2xl flex items-center justify-center mb-6 shadow-lg group-hover:scale-110 transition-transform">
                            <span className="text-4xl">🖥️</span>
                        </div>
                        <h2 className="text-3xl font-bold mb-3">Desktop Admin</h2>
                        <p className="text-blue-100 leading-relaxed">
                            Full ERP access for office administration, inventory management, reports, and logistics tracking.
                        </p>
                        <div className="mt-8 px-6 py-2 bg-white/20 rounded-full text-sm font-medium group-hover:bg-white/30 transition-colors">
                            Go to Dashboard →
                        </div>
                    </div>
                </Link>

                {/* MOBILE / SALES CARD */}
                <Link href="/sales" className="group block">
                    <div className="bg-white/10 backdrop-blur-md border border-white/20 hover:bg-white/20 hover:border-white/40 rounded-2xl p-10 flex flex-col items-center text-center transition-all duration-300 transform hover:-translate-y-2 shadow-xl h-full">
                        <div className="w-24 h-24 bg-gradient-to-tr from-green-500 to-emerald-400 rounded-2xl flex items-center justify-center mb-6 shadow-lg group-hover:scale-110 transition-transform">
                            <span className="text-4xl">📱</span>
                        </div>
                        <h2 className="text-3xl font-bold mb-3">Mobile Sales</h2>
                        <p className="text-blue-100 leading-relaxed">
                            Streamlined interface for fast order entry, seller actions, and on-the-go management.
                        </p>
                        <div className="mt-8 px-6 py-2 bg-white/20 rounded-full text-sm font-medium group-hover:bg-white/30 transition-colors">
                            Open Sales App →
                        </div>
                    </div>
                </Link>
            </div>

            <div className="mt-16 text-blue-300/60 text-sm font-medium">
                © 2026 AGF Software Solutions • Fast & Secure
            </div>
        </div>
    );
}
