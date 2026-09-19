import type { LucideIcon } from "lucide-react"
import { Zap, Grid2x2, Layers, Wifi, Disc, Puzzle, Smartphone, MoonStar, LifeBuoy } from "lucide-react"

import { Badge } from "@/components/ui/badge"

type Feature = {
  icon: LucideIcon
  title: string
  description: string
  beta?: boolean
}

const features: Feature[] = [
  {
    icon: Zap,
    title: "Faible latence",
    description: "Décodage FFmpeg basse latence, dernière frame prioritaire, audio ~400 ms, sockets optimisés.",
  },
  {
    icon: Grid2x2,
    title: "Multicompte",
    description: "Plusieurs téléphones dans une seule fenêtre — un compte par téléphone connecté, le modèle autorisé par Ankama.",
  },
  {
    icon: Layers,
    title: "Espaces de travail",
    description: "Un espace par session, compte ou activité — chacun garde ses miroirs et sa disposition, bascule en un clic.",
  },
  {
    icon: Wifi,
    title: "USB & WiFi",
    description: "Bascule en sans-fil en un clic, puis débranche le câble — le flux continue.",
  },
  {
    icon: Disc,
    title: "Enregistrement & OBS",
    description: "MP4 sans ré-encodage, captures PNG, mode capture propre pour les streamers.",
  },
  {
    icon: Puzzle,
    title: "Plugins & marketplace",
    description: "Extensions sandboxées en un clic — Almanax, HUD, checklist. Catalogue embarqué, plugins officiels signés.",
  },
  {
    icon: Smartphone,
    title: "iPhone · AirPlay",
    description: "L'iPhone rejoint le miroir — récepteur AirPlay maison, en Wi-Fi, sans câble. Affichage en cours de finalisation.",
    beta: true,
  },
  {
    icon: MoonStar,
    title: "Écran éteint",
    description: "L'écran du téléphone passe au noir pendant le mirroring — économise la batterie et l'AMOLED.",
  },
  {
    icon: LifeBuoy,
    title: "Aide intégrée",
    description: "Almanax, guides papycha, Atlas, encyclopédie — consultables sans quitter le jeu.",
  },
]

export function FeaturesGrid() {
  return (
    <section id="fonctionnalites" className="px-6 py-16">
      <div className="mx-auto max-w-4xl">
        <h2 className="text-2xl font-semibold tracking-tight">Pensé pour Dofus Touch</h2>
        <p className="mt-2 max-w-xl text-sm text-muted-foreground">
          Branche, clique, joue. Le jeu officiel tourne sur ton téléphone — TouchMirror l&apos;affiche et le
          contrôle depuis le PC via un pipeline GPU zéro-copie. L&apos;alternative open source à scrcpy,
          pensée pour les joueurs.
        </p>

        <div className="mt-8 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {features.map((feature) => (
            <div
              key={feature.title}
              className="rounded-xl border border-border bg-card p-5 transition-colors hover:border-primary/35"
            >
              <div className="flex size-8 items-center justify-center rounded-lg bg-primary/10">
                <feature.icon className="size-4 text-primary" />
              </div>
              <div className="mt-3 flex items-center gap-2">
                <h3 className="text-sm font-semibold">{feature.title}</h3>
                {feature.beta && (
                  <Badge variant="outline" className="border-primary/40 text-primary">
                    bêta
                  </Badge>
                )}
              </div>
              <p className="mt-1.5 text-xs leading-relaxed text-muted-foreground">{feature.description}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}
