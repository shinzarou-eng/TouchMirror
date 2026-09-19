import { ShieldCheck } from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { SectionHeading } from "@/components/section-heading"
import { Reveal, RevealGroup, RevealItem } from "@/components/reveal"
import { site } from "@/lib/site"

const steps = [
  {
    n: "01",
    title: "Branche en USB",
    description: "Un câble data classique. adb est embarqué — rien à installer sur le PC.",
  },
  {
    n: "02",
    title: "Débogage USB",
    description: "Options développeur → Débogage USB, puis autorise le PC sur le téléphone.",
  },
  {
    n: "03",
    title: "Connecter → Dofus",
    description: "Le miroir s'affiche, le bouton Dofus lance le jeu. C'est tout.",
  },
]

export function HowItWorks() {
  return (
    <section id="installation" className="border-b border-border px-6 py-16">
      <div className="mx-auto max-w-4xl">
        <SectionHeading kicker="Installation" title="Prêt en 3 étapes" />

        <RevealGroup className="mt-8 grid gap-px overflow-hidden rounded-lg border border-border bg-border sm:grid-cols-3">
          {steps.map((step) => (
            <RevealItem key={step.n}>
              <div className="h-full bg-card p-5">
                <span className="font-mono text-xs text-muted-foreground/60">{step.n}</span>
                <h3 className="mt-2 text-sm font-semibold">{step.title}</h3>
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
