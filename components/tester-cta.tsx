import { FlaskConical } from "lucide-react"

import { Button } from "@/components/ui/button"
import { DiscordIcon } from "@/components/icons/discord-icon"
import { Reveal } from "@/components/reveal"
import { site } from "@/lib/site"

export function TesterCta() {
  return (
    <section className="border-b border-border px-6 py-12">
      <Reveal className="mx-auto flex max-w-3xl flex-col items-start gap-5 rounded-lg border border-border bg-card p-6 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <FlaskConical className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <div>
            <h2 className="text-sm font-semibold">On cherche des testeurs</h2>
            <p className="mt-1.5 max-w-md text-sm leading-relaxed text-muted-foreground">
              TouchMirror est jeune et avance vite. Rejoins le Discord pour remonter un bug, proposer un
              plugin ou tester en multicompte — ce que la communauté demande passe en priorité sur la
              roadmap.
            </p>
          </div>
        </div>
        <Button asChild className="shrink-0 bg-[#5865F2] text-white hover:bg-[#6A75F5]">
          <a href={site.discordUrl}>
            <DiscordIcon data-icon="inline-start" className="size-4" />
            Rejoindre le Discord
          </a>
        </Button>
      </Reveal>
    </section>
  )
}
