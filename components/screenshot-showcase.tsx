import Image from "next/image"

import { Reveal } from "@/components/reveal"

export function ScreenshotShowcase() {
  return (
    <section className="border-b border-border px-6 py-16">
      <Reveal className="mx-auto max-w-4xl">
        <div className="overflow-hidden rounded-lg border border-border bg-card shadow-xl shadow-black/30">
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
