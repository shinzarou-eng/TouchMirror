import Image from "next/image"

import { Reveal } from "@/components/reveal"

const screenshots = [
  {
    src: "/images/touchmirror-connected.png",
    alt: "TouchMirror connecté à un appareil Android avec Dofus Touch affiché",
    label: "Session active",
    description: "L'écran Android reste visible dans une fenêtre dédiée, avec les contrôles TouchMirror à portée de main.",
  },
  {
    src: "/images/touchmirror-login.png",
    alt: "Écran d'accueil Dofus Touch affiché dans TouchMirror",
    label: "Accueil Android",
    description: "Retrouvez vos comptes et lancez votre session depuis le bureau, sans changer de contexte.",
  },
]

export function ScreenshotShowcase() {
  return (
    <section className="border-b border-border px-6 py-16">
      <Reveal className="mx-auto max-w-6xl">
        <div className="mb-8 flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
          <div>
            <p className="font-mono text-xs uppercase tracking-[0.2em] text-muted-foreground">Aperçu du produit</p>
            <h2 className="mt-3 max-w-xl text-balance text-2xl font-semibold tracking-tight text-foreground md:text-3xl">
              Votre téléphone, lisible depuis le bureau.
            </h2>
          </div>
          <p className="max-w-sm text-pretty text-sm leading-6 text-muted-foreground">
            Deux vues réelles de TouchMirror pour montrer ce que vous obtenez dès la première connexion.
          </p>
        </div>

        <div className="grid gap-5 lg:grid-cols-[1.15fr_0.85fr]">
          {screenshots.map((screenshot, index) => (
            <figure key={screenshot.src} className={index === 0 ? "overflow-hidden rounded-lg border border-border bg-card" : "overflow-hidden rounded-lg border border-border bg-card lg:mt-12"}>
              <div className="flex items-center justify-between border-b border-border bg-secondary/60 px-4 py-2.5">
                <span className="font-mono text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{screenshot.label}</span>
                <span className="font-mono text-[11px] text-muted-foreground/60">0{index + 1}</span>
              </div>
              <Image
                src={screenshot.src}
                alt={screenshot.alt}
                width={1920}
                height={1153}
                className="h-auto w-full"
                sizes="(min-width: 1024px) 640px, 100vw"
              />
              <figcaption className="border-t border-border px-4 py-4 text-sm leading-6 text-muted-foreground">
                {screenshot.description}
              </figcaption>
            </figure>
          ))}
        </div>
      </Reveal>
    </section>
  )
}
