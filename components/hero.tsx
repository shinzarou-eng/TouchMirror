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
    <section id="top" className="relative overflow-hidden px-6 pt-24 pb-16 text-center sm:pt-32">
      <div
        className="bg-grid pointer-events-none absolute inset-0 [mask-image:radial-gradient(ellipse_60%_55%_at_50%_0%,black,transparent)]"
        aria-hidden="true"
      />
      <div
        className="pointer-events-none absolute top-[-160px] left-1/2 h-[420px] w-[720px] -translate-x-1/2 rounded-full bg-primary/[0.14] blur-[110px]"
        aria-hidden="true"
      />

      <RevealGroup className="relative mx-auto max-w-2xl">
        <RevealItem>
          <Image
            src="/images/mascot.png"
            alt="Mascotte TouchMirror"
            width={88}
            height={88}
            priority
            className="mx-auto drop-shadow-[0_0_50px_rgba(78,201,142,0.35)]"
          />
        </RevealItem>

        <RevealItem>
          <h1 className="mt-7 font-sans text-5xl font-light tracking-tight text-balance sm:text-6xl">
            Touch<span className="text-glow font-semibold text-primary">Mirror</span>
          </h1>
        </RevealItem>

        <RevealItem>
          <p className="mt-5 text-lg text-muted-foreground text-balance sm:text-xl">
            Joue à Dofus Touch sur PC, depuis ton vrai téléphone.
          </p>
        </RevealItem>

        <RevealItem>
          <p className="mt-2 text-sm text-muted-foreground/70">
            Gratuit · Open source · Pas un émulateur · Pas d&apos;automatisation
          </p>
          <p className="mt-1 font-mono text-xs text-muted-foreground/50">
            Android screen mirroring for Windows — control your phone from your PC
          </p>
        </RevealItem>

        <RevealItem className="mt-9 flex flex-wrap items-center justify-center gap-3">
          <Button asChild size="lg" className="shadow-glow">
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
            Windows 10/11 64 bits · rien à installer ·{" "}
            <a href={site.licenseUrl} className="underline underline-offset-2 hover:text-foreground">
              licence MIT
            </a>
          </p>
        </RevealItem>
      </RevealGroup>

      <Suspense fallback={<div className="mt-9 h-12" />}>
        <TrustBar />
      </Suspense>
    </section>
  )
}
