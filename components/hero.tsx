import { Suspense } from "react"
import Image from "next/image"
import { Download } from "lucide-react"

import { Button } from "@/components/ui/button"
import { DiscordIcon } from "@/components/icons/discord-icon"
import { GithubIcon } from "@/components/icons/github-icon"
import { TrustBar } from "@/components/trust-bar"
import { RevealGroup, RevealItem } from "@/components/reveal"
import { site } from "@/lib/site"

export function Hero() {
  return (
    <section id="top" className="border-b border-border px-6 pt-20 pb-14 sm:pt-28">
      <RevealGroup className="mx-auto max-w-2xl text-center">
        <RevealItem className="flex items-center justify-center gap-2">
          <Image src="/images/mascot.png" alt="" width={20} height={20} className="rounded-sm" priority />
          <span className="font-mono text-xs tracking-wide text-muted-foreground">
            Windows 10/11 · gratuit · open source
          </span>
        </RevealItem>

        <RevealItem>
          <h1 className="mt-6 text-4xl font-semibold tracking-tight text-balance sm:text-5xl">
            Dofus Touch mirroring sur PC
          </h1>
          <p className="mt-2 font-mono text-xs uppercase tracking-[0.18em] text-primary">
            TouchMirror
          </p>
        </RevealItem>

        <RevealItem>
          <p className="mt-4 text-lg text-muted-foreground text-balance">
            Joue à Dofus Touch sur PC depuis ton vrai téléphone Android.
          </p>
        </RevealItem>

        <RevealItem>
          <p className="mt-2 text-sm text-muted-foreground/70">
            Dofus Touch mirroring Android natif. Pas un émulateur, pas d&apos;automatisation.
          </p>
        </RevealItem>

        <RevealItem className="mt-8 flex flex-wrap items-center justify-center gap-3">
          <Button asChild size="lg">
            <a href={site.releasesUrl}>
              <Download data-icon="inline-start" />
              Télécharger pour Windows
            </a>
          </Button>
          <Button asChild size="lg" className="bg-[#5865F2] text-white hover:bg-[#6A75F5]">
            <a href={site.discordUrl}>
              <DiscordIcon data-icon="inline-start" className="size-4" />
              Rejoindre le Discord
            </a>
          </Button>
          <Button asChild size="lg" variant="outline">
            <a href={site.repoUrl}>
              <GithubIcon data-icon="inline-start" className="size-4" />
              Voir le code source
            </a>
          </Button>
        </RevealItem>

        <RevealItem>
          <p className="mt-5 font-mono text-xs text-muted-foreground/60">
            Rien à installer sur le téléphone ·{" "}
            <a href={site.licenseUrl} className="underline underline-offset-2 hover:text-foreground">
              licence MIT
            </a>
          </p>
        </RevealItem>
      </RevealGroup>

      <Suspense fallback={<div className="mt-10 h-10" />}>
        <TrustBar />
      </Suspense>
    </section>
  )
}
