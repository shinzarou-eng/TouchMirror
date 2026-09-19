import { ShieldCheck } from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { SectionHeading } from "@/components/section-heading"
import { Reveal, RevealGroup, RevealItem } from "@/components/reveal"
import { site } from "@/lib/site"

const steps = [
  {
    n: "1",
    title: "Branche en USB",
    description: "Un câble data classique. adb est embarqué — rien à installer sur le PC.",
  },
  {
    n: "2",
    title: "Débogage USB",
    description: "Options développeur → Débogage USB, puis autorise le PC sur le téléphone.",
  },
  {
    n: "3",
    title: "Connecter → Dofus",
    description: "Le miroir s'affiche, le bouton Dofus lance le jeu. C'est tout.",
  },
]

export function HowItWorks() {
  return (
    <section id="installation" className="px-6 py-16">
      <div className="mx-auto max-w-4xl">
        <SectionHeading kicker="Installation" title="Prêt en 3 étapes" />

        <RevealGroup className="relative mt-8 grid gap-3 sm:grid-cols-3">
          <div
            className="absolute top-[27px] right-[16.5%] left-[16.5%] hidden h-px bg-gradient-to-r from-primary/40 via-primary/15 to-primary/40 sm:block"
            aria-hidden="true"
          />
          {steps.map((step) => (
            <RevealItem key={step.n}>
              <div className="relative h-full rounded-xl border border-border bg-card p-5 transition-colors hover:border-primary/35">
                <div className="flex size-6 items-center justify-center rounded-full bg-primary/15 font-mono text-xs font-bold text-primary ring-4 ring-background">
                  {step.n}
                </div>
                <h3 className="mt-3.5 text-sm font-semibold">{step.title}</h3>
                <p className="mt-1.5 text-xs leading-relaxed text-muted-foreground">{step.description}</p>
              </div>
            </RevealItem>
          ))}
        </RevealGroup>

        <Reveal delay={0.1}>
          <Alert className="mt-6 border-border bg-card">
            <ShieldCheck className="text-primary" />
            <AlertTitle>Dans les règles du jeu.</AlertTitle>
            <AlertDescription className="leading-relaxed">
              TouchMirror n&apos;est ni un émulateur, ni un client modifié, ni un outil d&apos;automatisation —
              aucune macro, aucun replay, aucune lecture mémoire. Chaque action correspond à un geste humain
              transmis à ton vrai téléphone — et en multicompte, un compte par téléphone physique connecté.
              C&apos;est le cas d&apos;usage confirmé comme autorisé par le{" "}
              <a href={site.ankamaFaqUrl} className="underline underline-offset-2 hover:text-foreground">
                support Ankama
              </a>
              .
            </AlertDescription>
          </Alert>
        </Reveal>
      </div>
    </section>
  )
}
