"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/orders", label: "Orders" },
  { href: "/invoices", label: "Invoices" },
  { href: "/products", label: "Products" },
];

export default function Nav() {
  const pathname = usePathname();
  return (
    <nav className="w-full border-b border-gray-200 bg-white">
      <div className="mx-auto max-w-6xl px-4">
        <div className="flex h-12 items-center gap-6">
          <div className="font-semibold">AGF Admin</div>
          <ul className="flex items-center gap-4 text-sm">
            {links.map((l) => (
              <li key={l.href}>
                <Link
                  href={l.href}
                  className={`px-2 py-1 hover:text-blue-700 ${pathname === l.href ? "text-blue-700 font-semibold" : "text-gray-900"}`}
                >
                  {l.label}
                </Link>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </nav>
  );
} 