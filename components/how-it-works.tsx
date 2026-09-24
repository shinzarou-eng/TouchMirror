import { Download, Cable, Smartphone, Monitor } from "lucide-react"
import { site } from "@/lib/site"

const steps = [
  {
    icon: Cable,
    title: "Branche ton téléphone.",
    description:
      "Installe TouchMirror sur Windows, puis relie ton Android au PC avec un câble USB qui transfère les données. ADB est fourni avec l’app.",
  },
  {
    icon: Smartphone,
    title: "Autorise la connexion.",
    description:
      "Active le débogage USB dans les options développeur d’Android. Sur le téléphone, accepte la demande d’autorisation de ton PC.",
  },
  {
    icon: Monitor,
    title: "Ouvre ton miroir.",
    description:
      "Sélectionne le téléphone dans TouchMirror, clique sur Connecter et ouvre Dofus Touch. Le jeu tourne toujours sur ton appareil.",
  },
]

export function HowItWorks() {
  return (
    <section id="installation" className="section-space installation-section">
      <div className="site-container">
        <div className="section-intro">
          <h2>
            On branche.
            <br />
            Et on s’installe.
          </h2>
          <div>
            <p>
              Pour la première connexion, commence en USB. Un téléphone, un
              câble et ton PC.
            </p>
            <a className="text-link" href={site.docsUrl}>
              Ouvrir le guide d’installation <span aria-hidden="true">↗</span>
            </a>
          </div>
        </div>
        <ol className="installation-steps">
          {steps.map(({ icon: Icon, title, description }, index) => (
            <li key={title}>
              <div className="step-heading">
                <span className="step-number">{index + 1}</span>
                <Icon size={24} aria-hidden="true" />
              </div>
              <h3>{title}</h3>
              <p>{description}</p>
            </li>
          ))}
        </ol>
        <div className="installation-download">
          <a href={site.releasesUrl} className="action-primary">
            <Download size={18} aria-hidden="true" /> Télécharger TouchMirror
          </a>
          <span>Gratuit. Le code est ouvert, lui aussi.</span>
        </div>
      </div>
    </section>
  )
}
