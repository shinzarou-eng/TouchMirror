import Image from "next/image"
import { DiscordIcon } from "@/components/icons/discord-icon"
import { GithubIcon } from "@/components/icons/github-icon"
import { site } from "@/lib/site"

export function TesterCta() {
  return (
    <section id="communaute" className="community-section">
      <div className="site-container community-layout">
        <div className="community-mascot">
          <Image
            src="/images/mascot.png"
            alt=""
            width={180}
            height={180}
            sizes="180px"
          />
        </div>
        <div>
          <h2>
            Ça se construit
            <br />
            avec ceux qui jouent.
          </h2>
          <p>
            TouchMirror est un jeune projet. Un téléphone qui se connecte mal,
            un détail qui manque, une idée pour la prochaine version : viens en
            parler. Les retours concrets font avancer l’app.
          </p>
          <div className="community-actions">
            <a href={site.discordUrl} className="action-primary">
              <DiscordIcon className="size-5" aria-hidden="true" /> Rejoindre le
              Discord
            </a>
            <a href={site.repoUrl} className="text-link">
              <GithubIcon className="size-5" aria-hidden="true" /> Voir le code
              sur GitHub
            </a>
          </div>
        </div>
      </div>
    </section>
  )
}
