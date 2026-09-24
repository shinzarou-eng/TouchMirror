"use client"

import { useState } from "react"
import Image from "next/image"
import { Download, Menu, X } from "lucide-react"
import { site } from "@/lib/site"

const navLinks = [
  { label: "L’app", href: "#apercu" },
  { label: "Installation", href: "#installation" },
  { label: "Questions", href: "#questions" },
  { label: "Communauté", href: "#communaute" },
]

export function SiteHeader() {
  const [menuOpen, setMenuOpen] = useState(false)
  return (
    <header
      className="site-header"
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          setMenuOpen(false)
          document.getElementById("menu-toggle")?.focus()
        }
      }}
    >
      <a className="skip-link" href="#main">
        Aller au contenu
      </a>
      <div className="site-container header-inner">
        <a
          href="#top"
          className="site-brand"
          aria-label="TouchMirror, accueil"
          onClick={() => setMenuOpen(false)}
        >
          <Image src="/images/mascot.png" alt="" width={34} height={34} />
          <span>
            Touch<span className="text-primary">Mirror</span>
          </span>
        </a>
        <nav className="desktop-nav" aria-label="Navigation principale">
          {navLinks.map((link) => (
            <a key={link.href} href={link.href}>
              {link.label}
            </a>
          ))}
        </nav>
        <a className="header-download" href={site.releasesUrl}>
          <Download size={16} aria-hidden="true" />
          <span>Télécharger</span>
        </a>
        <button
          id="menu-toggle"
          className="menu-toggle"
          aria-expanded={menuOpen}
          aria-controls="mobile-navigation"
          aria-label={menuOpen ? "Fermer le menu" : "Ouvrir le menu"}
          onClick={() => setMenuOpen(!menuOpen)}
        >
          {menuOpen ? <X size={22} /> : <Menu size={22} />}
        </button>
      </div>
      <nav
        id="mobile-navigation"
        className="mobile-nav"
        aria-label="Navigation mobile"
        hidden={!menuOpen}
      >
        {navLinks.map((link) => (
          <a
            key={link.href}
            href={link.href}
            onClick={() => setMenuOpen(false)}
          >
            {link.label}
          </a>
        ))}
        <a href={site.docsUrl}>Documentation</a>
      </nav>
    </header>
  )
}
