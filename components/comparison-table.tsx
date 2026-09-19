import { Check, Minus } from "lucide-react"

import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { SectionHeading } from "@/components/section-heading"
import { Reveal } from "@/components/reveal"

type Cell = boolean | "beta"

const rows: { feature: string; touchmirror: Cell; scrcpy: Cell; vysor: Cell; walky: Cell }[] = [
  { feature: "Interface Windows native", touchmirror: true, scrcpy: false, vysor: true, walky: true },
  { feature: "Plusieurs téléphones dans une fenêtre", touchmirror: true, scrcpy: false, vysor: false, walky: true },
  { feature: "Raccourcis plaqués (touche → tap)", touchmirror: true, scrcpy: false, vysor: false, walky: false },
  { feature: "Affichage virtuel / mode tablette", touchmirror: true, scrcpy: true, vysor: false, walky: true },
  { feature: "Multicompte sur un seul téléphone", touchmirror: true, scrcpy: false, vysor: false, walky: true },
  { feature: "iPhone / iOS", touchmirror: "beta", scrcpy: false, vysor: true, walky: true },
  { feature: "Pipeline GPU direct", touchmirror: true, scrcpy: false, vysor: false, walky: true },
  { feature: "Enregistrement MP4 intégré", touchmirror: true, scrcpy: true, vysor: false, walky: false },
  { feature: "API locale + plugins sandboxés", touchmirror: true, scrcpy: false, vysor: false, walky: false },
  { feature: "Open source", touchmirror: true, scrcpy: true, vysor: false, walky: false },
]

function CellIcon({ value }: { value: Cell }) {
  if (value === "beta") {
    return <span className="font-mono text-xs font-medium text-primary">bêta</span>
  }
  if (value) {
    return <Check className="size-4 text-primary" aria-label="Oui" />
  }
  return <Minus className="size-4 text-muted-foreground/40" aria-label="Non" />
}

export function ComparisonTable() {
  return (
    <section id="comparatif" className="px-6 py-16">
      <div className="mx-auto max-w-4xl">
        <SectionHeading
          kicker="Comparatif"
          title="Chaque outil a ses forces"
          description="Voici où se situe TouchMirror face aux alternatives les plus connues."
        />

        <Reveal>
          <div className="mt-6 overflow-x-auto rounded-xl border border-border shadow-glow">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="text-muted-foreground">Fonctionnalité</TableHead>
                  <TableHead className="bg-primary/[0.06] text-center font-semibold text-primary">
                    TouchMirror
                  </TableHead>
                  <TableHead className="text-center text-muted-foreground">scrcpy</TableHead>
                  <TableHead className="text-center text-muted-foreground">Vysor</TableHead>
                  <TableHead className="text-center text-muted-foreground">Walky</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.feature}>
                    <TableCell className="text-sm">{row.feature}</TableCell>
                    <TableCell className="bg-primary/[0.06] text-center">
                      <div className="flex justify-center">
                        <CellIcon value={row.touchmirror} />
                      </div>
                    </TableCell>
                    <TableCell className="text-center">
                      <div className="flex justify-center">
                        <CellIcon value={row.scrcpy} />
                      </div>
                    </TableCell>
                    <TableCell className="text-center">
                      <div className="flex justify-center">
                        <CellIcon value={row.vysor} />
                      </div>
                    </TableCell>
                    <TableCell className="text-center">
                      <div className="flex justify-center">
                        <CellIcon value={row.walky} />
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </Reveal>
        <p className="mt-3 text-xs text-muted-foreground/60">
          Situation au 16/09/2026 — ouvre une issue si quelque chose a changé.
        </p>
      </div>
    </section>
  )
}
