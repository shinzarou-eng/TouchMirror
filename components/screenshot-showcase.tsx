import Image from "next/image"

export function ScreenshotShowcase() {
  return (
    <section className="px-6 py-16">
      <div className="mx-auto max-w-4xl overflow-hidden rounded-2xl border border-border shadow-[0_30px_80px_rgba(0,0,0,0.55),0_0_0_1px_rgba(78,201,142,0.06)]">
        <Image
          src="/images/screenshot.png"
          alt="TouchMirror — Dofus Touch en cours de jeu sur PC"
          width={1280}
          height={800}
          className="w-full"
          sizes="(min-width: 1024px) 896px, 100vw"
        />
      </div>
    </section>
  )
}
