import { Plus } from "lucide-react"
import { site } from "@/lib/site"

const questions = [
  {
    title: "C’est un émulateur ?",
    answer: (
      <>
        Non. Dofus Touch tourne sur ton vrai téléphone Android. TouchMirror
        affiche son écran sur Windows et transmet tes gestes de souris et de
        clavier. Il ne remplace pas le jeu.
      </>
    ),
  },
  {
    title: "Comment fonctionne le multicompte ?",
    answer: (
      <>
        Deux façons : connecte plusieurs téléphones, ou crée plusieurs
        comptes sur un même téléphone — chacun a son Dofus Touch indépendant
        via un profil Android dédié. Tu contrôles le miroir sélectionné.
        Une action n’est jamais diffusée à plusieurs appareils.
      </>
    ),
  },
  {
    title: "Et les règles d’Ankama ?",
    answer: (
      <>
        TouchMirror ne propose ni bot, ni macro, ni automatisation de jeu. Le
        projet est indépendant et n’est pas affilié à Ankama. Consulte la{" "}
        <a href={site.ankamaFaqUrl}>FAQ officielle d’Ankama</a> pour les règles
        applicables à ton usage.
      </>
    ),
  },
  {
    title: "Est-ce que ça va bien tourner sur mon PC ?",
    answer: (
      <>
        Cela dépend du PC, du téléphone et de la connexion. Commence en USB,
        puis utilise le benchmark intégré pour observer les performances de ta
        session. En WiFi, le diagnostic réseau aide à chercher l’origine des
        ralentissements.
      </>
    ),
  },
  {
    title: "Je peux jouer sur iPhone ou sur Mac ?",
    answer: (
      <>
        iPhone : en phase expérimentale. TouchMirror embarque un récepteur
        AirPlay natif — Centre de contrôle → Recopie de l’écran →
        « TouchMirror », appairage par code à 4 chiffres et contrôle tactile
        via Bluetooth (AssistiveTouch). La fonctionnalité est jeune :
        attends-toi à des imperfections, et remonte ce que tu observes sur
        Discord. macOS n’est pas pris en charge.
      </>
    ),
  },
  {
    title: "La connexion ne fonctionne pas. Je fais quoi ?",
    answer: (
      <>
        Vérifie le câble et l’autorisation de débogage sur ton téléphone. Ouvre
        ensuite Diagnostic dans TouchMirror. Si tu demandes de l’aide sur
        Discord, relis le rapport avant de le partager et masque tes adresses
        IP, numéros de série et chemins personnels.
      </>
    ),
  },
]

export function ExtrasTable() {
  return (
    <section id="questions" className="section-space faq-section">
      <div className="site-container faq-layout">
        <div>
          <h2>Avant de te lancer.</h2>
          <p>Les questions qu’on se poserait à ta place.</p>
          <a href={site.docsUrl} className="text-link">
            Toute la documentation <span aria-hidden="true">↗</span>
          </a>
        </div>
        <div className="faq-list">
          {questions.map(({ title, answer }) => (
            <details key={title}>
              <summary>
                {title}
                <Plus size={19} aria-hidden="true" />
              </summary>
              <div className="faq-answer">
                <p>{answer}</p>
              </div>
            </details>
          ))}
        </div>
      </div>
    </section>
  )
}
