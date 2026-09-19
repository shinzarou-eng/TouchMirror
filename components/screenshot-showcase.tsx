import Image from "next/image"

import { Reveal } from "@/components/reveal"

export function ScreenshotShowcase() {
  return (
    <section className="relative px-6 py-16">
      <div
        className="pointer-events-none absolute top-1/2 left-1/2 h-[360px] w-[820px] -translate-x-1/2 -translate-y-1/2 rounded-full bg-primary/[0.08] blur-[100px]"
        aria-hidden="true"
      />

      <Reveal className="relative mx-auto max-w-4xl">
        <div className="shadow-glow-lg overflow-hidden rounded-2xl border border-border bg-card">
          <div className="flex items-center gap-2 border-b border-border bg-secondary/60 px-4 py-2.5">
            <span className="size-2.5 rounded-full bg-muted-foreground/25" />
            <span className="size-2.5 rounded-full bg-muted-foreground/25" />
            <span className="size-2.5 rounded-full bg-muted-foreground/25" />
            <span className="ml-2 font-mono text-[11px] text-muted-foreground/60">
              TouchMirror — Dofus Touch
            </span>
          </div>
          <Image
            src="/images/screenshot.png"
            alt="TouchMirror — Dofus Touch en cours de jeu sur PC"
            width={1280}
            height={800}
            className="w-full"
            sizes="(min-width: 1024px) 896px, 100vw"
          />
        </div>
      </Reveal>
    </section>
  )
}
