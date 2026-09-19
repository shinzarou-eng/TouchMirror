import { site } from "@/lib/site"

export function SiteFooter() {
  return (
    <footer className="border-t border-border px-6 py-8">
      <div className="mx-auto flex max-w-4xl flex-col gap-2 text-xs text-muted-foreground/70 sm:flex-row sm:items-center">
        <span>
          TouchMirror —{" "}
          <a href={site.repoUrl} className="underline underline-offset-2 hover:text-foreground">
            code source
          </a>{" "}
          sous licence MIT
        </span>
        <span className="flex-1" />
        <span>Sans lien avec Ankama. DOFUS et DOFUS Touch sont des marques d&apos;Ankama.</span>
      </div>
    </footer>
  )
}
