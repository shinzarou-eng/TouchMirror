import { Cable, Layers, MousePointer2, Puzzle, Smartphone, Video } from "lucide-react"

const features = [
  {
    icon: Smartphone,
    title: "Android et iPhone, même grille.",
    badge: "iOS expérimental",
    description:
      "Android en USB ou WiFi. iPhone en AirPlay natif — les deux côte à côte, avec les mêmes réglages.",
  },
  {
    icon: Cable,
    title: "Un câble pour commencer.",
    description:
      "Branche ton téléphone et l’assistant première connexion te guide pas à pas, jusqu’au premier miroir.",
  },
  {
    icon: Layers,
    title: "Ta session, bien rangée.",
    description:
      "Regroupe tes miroirs dans des espaces de travail, multiplie les comptes sur un même téléphone, retrouve tout en place.",
  },
  {
    icon: MousePointer2,
    title: "C’est toujours toi qui joues.",
    description:
      "La souris et le clavier contrôlent le téléphone sélectionné. Pas de bot, de macro ni de clic répliqué sur plusieurs appareils.",
  },
  {
    icon: Puzzle,
    title: "Des plugins, en sandbox.",
    description:
      "Archimonstres, almanax, mixeur audio, minuteurs — installe les extensions du marketplace ; elles tournent isolées, sans accès réseau ni input.",
  },
  {
    icon: Video,
    title: "Ce moment mérite une capture.",
    description:
      "Prends une capture ou enregistre ton miroir depuis l’app. Pour garder un souvenir, montrer un problème ou partager ta session.",
  },
]

export function FeaturesGrid() {
  return (
    <section id="fonctionnalites" className="section-space everyday-section">
      <div className="site-container everyday-layout">
        <div className="everyday-heading">
          <h2>
            Les petits détails
            <br />
            font les bonnes sessions.
          </h2>
          <p>
            Pas besoin de tout réinventer.
            <br />
            Juste de rendre le quotidien plus agréable.
          </p>
        </div>
        <div className="feature-list">
          {features.map(({ icon: Icon, title, badge, description }) => (
            <article key={title} className="feature-row">
              <Icon size={22} aria-hidden="true" />
              <div>
                <h3>
                  {title}
                  {badge && <span className="tag-beta">{badge}</span>}
                </h3>
                <p>{description}</p>
              </div>
            </article>
          ))}
        </div>
      </div>
    </section>
  )
}
