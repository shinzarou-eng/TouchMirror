import Image from "next/image"
import { Download } from "lucide-react"

import { Button } from "@/components/ui/button"
import { DiscordIcon } from "@/components/icons/discord-icon"
import { GithubIcon } from "@/components/icons/github-icon"
import { site } from "@/lib/site"

export function Hero() {
  return (
    <section id="top" className="px-6 pt-20 pb-16 text-center sm:pt-24">
      <div className="mx-auto max-w-2xl">
        <Image
          src="/images/mascot.png"
          alt="Mascotte TouchMirror"
          width={96}
          height={96}
          priority
          className="mx-auto drop-shadow-[0_0_42px_rgba(78,201,142,0.18)]"
        />

        <h1 className="mt-6 font-sans text-4xl font-light tracking-tight text-balance sm:text-5xl">
          Touch<span className="font-semibold text-primary">Mirror</span>
        </h1>

        <p className="mt-4 text-lg text-muted-foreground text-balance">
          Joue à Dofus Touch sur PC, depuis ton vrai téléphone.
        </p>

        <p className="mt-2 text-sm text-muted-foreground/70">
          Gratuit · Open source · Pas un émulateur · Pas d&apos;automatisation
        </p>
        <p className="mt-1 font-mono text-xs text-muted-foreground/50">
          Android screen mirroring for Windows — control your phone from your PC
        </p>

        <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
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
        </div>

        <p className="mt-5 font-mono text-xs text-muted-foreground/60">
          Windows 10/11 64 bits · rien à installer ·{" "}
          <a href={site.licenseUrl} className="underline underline-offset-2 hover:text-foreground">
            licence MIT
          </a>
        </p>
      </div>
    </section>
  )
}
