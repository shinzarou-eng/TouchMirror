"use client"

import { useRef, useState } from "react"
import Image from "next/image"
import { AnimatePresence, motion, useReducedMotion } from "motion/react"
import type { LucideIcon } from "lucide-react"
import { Activity, LayoutDashboard, Maximize2, Settings, Store, X } from "lucide-react"

const screenshots: {
  src: string
  alt: string
  label: string
  title: string
  description: string
  detail: string
  icon: LucideIcon
  width: number
  height: number
}[] = [
  {
    src: "/images/touchmirror-hub.png",
    alt: "Le hub TouchMirror avec la miniature du téléphone et les réglages de sa liaison",
    label: "Tes appareils",
    title: "Tout le monde est là.",
    description:
      "Retrouve tes téléphones et leurs miniatures au même endroit. Choisis celui que tu veux afficher, sans jongler entre les fenêtres.",
    detail:
      "Un compte par téléphone physique. Tu gardes la main sur chaque appareil.",
    icon: LayoutDashboard,
    width: 1600,
    height: 950,
  },
  {
    src: "/images/touchmirror-diag-dock.png",
    alt: "Le diagnostic TouchMirror avec son rapport, une piste de résolution et les boutons de test réseau et benchmark",
    label: "Le diagnostic",
    title: "Ça coince ? On regarde.",
    description:
      "Connexion, réseau WiFi, performances : les outils de diagnostic sont réunis dans l’app pour t’aider à comprendre ce qui se passe.",
    detail:
      "Lance un test, consulte les pistes proposées ou copie le rapport pour demander de l’aide.",
    icon: Activity,
    width: 1280,
    height: 800,
  },
  {
    src: "/images/touchmirror-marketplace.png",
    alt: "La marketplace TouchMirror : catalogue d’extensions avec Almanax, Bridge, Checklist, HUD et Reconnect",
    label: "La marketplace",
    title: "Des extensions, pas des bots.",
    description:
      "Almanax, HUD, checklist, reconnect : un catalogue d’extensions qui tournent dans leur coin, sans toucher à tes entrées.",
    detail:
      "Chaque plugin est isolé et contrôle l’affichage — jamais le jeu à ta place.",
    icon: Store,
    width: 1280,
    height: 800,
  },
  {
    src: "/images/touchmirror-settings.png",
    alt: "Les réglages TouchMirror par appareil : toggles, presets de qualité, résolution, fréquence, débit et codec",
    label: "Les réglages",
    title: "Réglé pour ton matos.",
    description:
      "Preset, résolution, fréquence, débit, codec : chaque téléphone garde ses propres réglages, mémorisés pour la prochaine session.",
    detail:
      "Un preset Dofus Touch équilibré est prêt dès l’installation — tu peux tout ajuster après.",
    icon: Settings,
    width: 1280,
    height: 800,
  },
]

export function ScreenshotShowcase() {
  const [active, setActive] = useState(0)
  const reducedMotion = useReducedMotion()
  const dialog = useRef<HTMLDialogElement>(null)
  const selected = screenshots[active]

  return (
    <section id="apercu" className="section-space product-tour">
      <div className="site-container">
        <div className="section-intro">
          <h2>
            Moins de fenêtres.
            <br />
            Plus de place pour jouer.
          </h2>
          <p>
            Le miroir, c’est le début. Autour, les outils dont tu as besoin pour
            préparer ta session et régler les petits imprévus.
          </p>
        </div>
        <div
          className="tour-controls"
          role="group"
          aria-label="Choisir une capture de l’application"
        >
          {screenshots.map((screenshot, index) => (
            <button
              key={screenshot.src}
              type="button"
              aria-pressed={active === index}
              aria-controls="product-view"
              onClick={() => setActive(index)}
            >
              {active === index && (
                <motion.span
                  className="tour-active"
                  layoutId="tour-active"
                  transition={{ duration: reducedMotion ? 0 : 0.25 }}
                />
              )}
              <screenshot.icon size={18} aria-hidden="true" />
              <span>{screenshot.label}</span>
            </button>
          ))}
        </div>
        <div id="product-view" className="tour-view">
          <div className="tour-image">
            <AnimatePresence mode="wait" initial={false}>
              <motion.div
                key={selected.src}
                initial={{ opacity: 0, y: reducedMotion ? 0 : 8 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0 }}
                transition={{ duration: reducedMotion ? 0 : 0.18 }}
              >
                <button
                  className="screenshot-button"
                  type="button"
                  onClick={() => dialog.current?.showModal()}
                  aria-label={`Agrandir la capture : ${selected.label}`}
                  aria-haspopup="dialog"
                >
                  <Image
                    src={selected.src}
                    alt={selected.alt}
                    width={selected.width}
                    height={selected.height}
                    sizes="(min-width: 1280px) 1200px, 100vw"
                    loading="eager"
                    className="tour-screenshot"
                  />
                  <span className="image-expand">
                    <Maximize2 size={16} aria-hidden="true" /> Agrandir
                  </span>
                </button>
              </motion.div>
            </AnimatePresence>
          </div>
          <div
            className="tour-description"
            aria-live="polite"
            aria-atomic="true"
          >
            <h3>{selected.title}</h3>
            <div>
              <p>{selected.description}</p>
              <p className="tour-detail">{selected.detail}</p>
            </div>
          </div>
        </div>
        <dialog
          ref={dialog}
          className="screenshot-dialog"
          aria-labelledby="capture-title"
          onClick={(event) => {
            if (event.target === event.currentTarget) dialog.current?.close()
          }}
        >
          <div className="dialog-toolbar">
            <h3 id="capture-title">{selected.label}</h3>
            <button
              type="button"
              onClick={() => dialog.current?.close()}
              aria-label="Fermer la capture"
              autoFocus
            >
              <X size={22} />
            </button>
          </div>
          <div className="dialog-image-scroll">
            <Image
              src={selected.src}
              alt={selected.alt}
              width={selected.width}
              height={selected.height}
              sizes="100vw"
              className="dialog-image"
            />
          </div>
          <p>
            Sur petit écran, fais défiler l’image horizontalement pour lire les
            détails.
          </p>
        </dialog>
      </div>
    </section>
  )
}
