import { FlaskConical } from "lucide-react"

import { Button } from "@/components/ui/button"
import { DiscordIcon } from "@/components/icons/discord-icon"
import { Reveal } from "@/components/reveal"
import { site } from "@/lib/site"

export function TesterCta() {
  return (
    <section className="px-6">
      <Reveal className="relative mx-auto max-w-3xl overflow-hidden rounded-2xl border border-[#5865F2]/35 bg-gradient-to-br from-[#5865F2]/12 to-[#5865F2]/[0.03] px-8 py-10 text-center">
        <div
          className="pointer-events-none absolute -top-20 left-1/2 h-56 w-56 -translate-x-1/2 rounded-full bg-[#5865F2]/20 blur-[80px]"
          aria-hidden="true"
        />

        <div className="relative mx-auto flex size-10 items-center justify-center rounded-full bg-[#5865F2]/15">
          <span className="absolute inline-flex size-2 -translate-y-3.5 translate-x-3.5">
            <span className="absolute inline-flex size-full animate-ping rounded-full bg-[#5865F2]/60" />
            <span className="relative inline-flex size-2 rounded-full bg-[#5865F2]" />
          </span>
          <FlaskConical className="size-5 text-[#5865F2]" />
        </div>
        <h2 className="relative mt-4 text-2xl font-semibold tracking-tight">On cherche des testeurs</h2>
        <p className="relative mx-auto mt-3 max-w-lg text-sm leading-relaxed text-muted-foreground">
          TouchMirror est jeune et avance vite — tes retours comptent vraiment. Rejoins le Discord pour
          remonter un bug, proposer un plugin, tester en multicompte ou juste dire ce qui te manque. Ce que
          la communauté demande passe en priorité sur la roadmap.
        </p>
        <Button asChild size="lg" className="relative mt-6 bg-[#5865F2] text-white hover:bg-[#6A75F5]">
          <a href={site.discordUrl}>
            <DiscordIcon data-icon="inline-start" className="size-4" />
            Rejoindre le Discord
          </a>
        </Button>
        <p className="relative mt-4 text-xs text-muted-foreground/60">
          Bugs, idées, tests multi-téléphones, plugins — tout est lu.
        </p>
      </Reveal>
    </section>
  )
}
