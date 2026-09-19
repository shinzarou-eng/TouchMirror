import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { SectionHeading } from "@/components/section-heading"
import { Reveal } from "@/components/reveal"

const extras: { label: string; detail: React.ReactNode }[] = [
  { label: "Souris & clavier", detail: "Clic = tactile, Ctrl+molette = pinch-to-zoom, texte tapé comme clavier Bluetooth" },
  { label: "Raccourcis plaqués", detail: "Une touche = un tap à position fixe — remapping 1:1, sans auto-repeat ni macro" },
  { label: "Espaces de travail", detail: "Contextes nommés — chaque espace garde ses miroirs et sa disposition" },
  { label: "Presse-papiers", detail: "Bidirectionnel — Ctrl+V colle sur le téléphone, copier sur le tel arrive sur le PC" },
  { label: "Plugins & API locale", detail: "Catalogue sandboxé + API HTTP/SSE sur localhost — Stream Deck, OBS, scripts" },
  {
    label: "iPhone · AirPlay",
    detail: (
      <>
        Récepteur maison en Wi-Fi — bêta, affichage en finalisation ·{" "}
        <a
          href="https://github.com/shinzarou-eng/TouchMirror/blob/main/docs/airplay.md"
          className="underline underline-offset-2 hover:text-foreground"
        >
          état d&apos;avancement
        </a>
      </>
    ),
  },
  { label: "Plein écran", detail: "F11 — barre de contrôle au survol du bord haut" },
  { label: "Mises à jour", detail: "Détection automatique des nouvelles versions au démarrage" },
  { label: "Codecs", detail: "H.264, H.265, AV1 — jusqu'à la résolution native et 120 fps" },
  { label: "Comptes", detail: "Aucun — aucune donnée collectée, tout reste en local" },
]

export function ExtrasTable() {
  return (
    <section className="px-6 py-16">
      <div className="mx-auto max-w-4xl">
        <SectionHeading kicker="Détails" title="Le reste" />

        <Reveal className="mt-6">
          <div className="overflow-x-auto rounded-lg border border-border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="text-muted-foreground">Fonction</TableHead>
                  <TableHead className="text-muted-foreground">Détail</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {extras.map((row) => (
                  <TableRow key={row.label}>
                    <TableCell className="whitespace-nowrap text-sm font-medium">{row.label}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">{row.detail}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Reveal>
      </div>
    </section>
  )
}
