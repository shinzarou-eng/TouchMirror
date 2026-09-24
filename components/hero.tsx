import { Suspense } from "react"
import { Download, MousePointer2, Smartphone, Cable } from "lucide-react"

import { HeroVideo } from "@/components/hero-video"
import { TrustBar } from "@/components/trust-bar"
import { site } from "@/lib/site"

export function Hero() {
  return (
    <section id="top" className="hero-section">
      <div className="site-container">
        <div className="hero-intro">
          <div className="hero-title">
            <p className="availability">
              <span aria-hidden="true" /> Android & iPhone sur Windows.
              Gratuit et open source.
            </p>
            <h1>
              Dofus Touch,
              <br />
              en grand.
            </h1>
          </div>
          <div className="hero-copy">
            <p>
              Affiche ton vrai téléphone sur ton PC — Android en USB ou
              WiFi, iPhone en AirPlay{" "}
              <span className="tag-beta">Expérimental</span> — et joue à la
              souris et au clavier.
            </p>
            <div className="hero-actions">
              <a href={site.releasesUrl} className="action-primary">
                <Download size={18} aria-hidden="true" /> Télécharger pour
                Windows
              </a>
              <a href="#apercu" className="text-link">
                Faire le tour de l’app <span aria-hidden="true">↗</span>
              </a>
            </div>
            <span className="hero-requirements">
              Windows 10 / 11 · Android ou iPhone · USB, WiFi ou AirPlay
            </span>
          </div>
        </div>

        <figure className="hero-product">
          <div className="product-caption">
            <span>
              <span className="status-dot" aria-hidden="true" /> Une vraie
              partie. Sur un vrai appareil.
            </span>
            <span>TouchMirror en direct</span>
          </div>
          <div className="hero-screen">
            <HeroVideo />
          </div>
          <figcaption className="connection-strip">
            <span>
              <Smartphone size={17} aria-hidden="true" /> Ton Android
            </span>
            <span className="connection-line" aria-hidden="true">
              <Cable size={16} />
            </span>
            <span>
              <MousePointer2 size={17} aria-hidden="true" /> Ton PC
            </span>
            <p>Un geste de ta main. Une action sur ton téléphone.</p>
          </figcaption>
        </figure>
        <Suspense fallback={<div className="h-20" />}>
          <TrustBar />
        </Suspense>
      </div>
    </section>
  )
}
