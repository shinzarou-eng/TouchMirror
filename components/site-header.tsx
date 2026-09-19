import Image from "next/image"
import Link from "next/link"
import { Download } from "lucide-react"

import { Button } from "@/components/ui/button"
import { site } from "@/lib/site"

const navLinks = [
  { label: "Fonctionnalités", href: "#fonctionnalites" },
  { label: "Installation", href: "#installation" },
  { label: "Comparatif", href: "#comparatif" },
  { label: "Docs", href: site.docsUrl },
]

export function SiteHeader() {
  return (
    <header className="sticky top-0 z-50 border-b border-border/60 bg-background/75 backdrop-blur-lg backdrop-saturate-150">
      <div className="mx-auto flex h-14 max-w-5xl items-center gap-2 px-6">
        <Link href="#top" className="flex items-center gap-2.5">
          <Image src="/images/mascot.png" alt="" width={24} height={24} className="rounded-md" priority />
          <span className="text-sm font-semibold tracking-tight">
            Touch<span className="text-primary">Mirror</span>
          </span>
        </Link>

        <nav className="ml-6 hidden items-center gap-6 md:flex" aria-label="Navigation principale">
          {navLinks.map((link) => (
            <a
              key={link.href}
              href={link.href}
              className="text-sm text-muted-foreground transition-colors hover:text-foreground"
            >
              {link.label}
            </a>
          ))}
        </nav>

        <div className="flex-1" />

        <Button asChild size="sm" variant="ghost" className="hidden sm:inline-flex">
          <a href={site.repoUrl}>Code source</a>
        </Button>
        <Button asChild size="sm">
          <a href={site.releasesUrl}>
            <Download data-icon="inline-start" />
            Télécharger
          </a>
        </Button>
      </div>
    </header>
  )
}
