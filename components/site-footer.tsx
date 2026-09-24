import Image from "next/image"

import { site } from "@/lib/site"

const columns = [
  {
    heading: "Produit",
    links: [
      { label: "Fonctionnalités", href: "#fonctionnalites" },
      { label: "Installation", href: "#installation" },
      { label: "Télécharger", href: site.releasesUrl },
    ],
  },
  {
    heading: "Communauté",
    links: [
      { label: "Discord", href: site.discordUrl },
      { label: "Signaler un bug", href: site.issuesUrl },
      { label: "Roadmap", href: site.roadmapUrl },
      { label: "Documentation", href: site.docsUrl },
    ],
  },
  {
    heading: "Projet",
    links: [
      { label: "Code source", href: site.repoUrl },
      { label: "Licence MIT", href: site.licenseUrl },
      { label: "FAQ Ankama", href: site.ankamaFaqUrl },
    ],
  },
]

export function SiteFooter() {
  return (
    <footer className="border-t border-border px-6 py-12">
      <div className="mx-auto max-w-[1200px]">
        <div className="grid grid-cols-2 gap-10 sm:grid-cols-[1.4fr_1fr_1fr_1fr]">
          <div className="col-span-2 sm:col-span-1">
            <div className="flex items-center gap-2">
              <span className="flex size-7 items-center justify-center rounded-md border border-border bg-card p-1">
                <Image
                  src="/images/mascot.png"
                  alt="Mascotte TouchMirror"
                  width={22}
                  height={22}
                  className="size-full rounded object-cover"
                />
              </span>
              <span className="text-sm font-semibold tracking-tight">
                Touch<span className="text-primary">Mirror</span>
              </span>
            </div>
            <p className="mt-3 max-w-[220px] text-xs leading-relaxed text-muted-foreground">
              Mirroring Android natif et open source pour jouer à Dofus Touch
              sur PC.
            </p>
          </div>

          {columns.map((column) => (
            <div key={column.heading}>
              <h3 className="text-xs font-semibold tracking-wide text-foreground">
                {column.heading}
              </h3>
              <ul className="mt-3 flex flex-col gap-2.5">
                {column.links.map((link) => (
                  <li key={link.label}>
                    <a
                      href={link.href}
                      className="text-xs text-muted-foreground transition-colors hover:text-foreground"
                    >
                      {link.label}
                    </a>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="mt-10 flex flex-col gap-2 border-t border-border pt-6 text-xs text-muted-foreground/70 sm:flex-row sm:items-center">
          <span>
            © 2026 TouchMirror —{" "}
            <a
              href={site.repoUrl}
              className="underline underline-offset-2 hover:text-foreground"
            >
              code source
            </a>{" "}
            sous licence MIT
          </span>
          <span className="flex-1" />
          <img
            src="https://api.visitorbadge.io/api/visitors?path=touchmirror.xyz&label=visites&labelColor=090d14&countColor=10b981&style=flat-square"
            alt="Nombre de visiteurs"
            className="h-5 w-auto rounded-sm opacity-80"
          />
          <span>
            Sans lien avec Ankama. DOFUS et DOFUS Touch sont des marques
            d&apos;Ankama.
          </span>
        </div>
      </div>
    </footer>
  )
}
