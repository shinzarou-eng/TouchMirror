import { Download, EyeOff, ShieldCheck, Star, Tag } from "lucide-react"

import { formatDownloads, formatStars, getRepoStats } from "@/lib/github"
import { site } from "@/lib/site"
import { GithubIcon } from "@/components/icons/github-icon"

export async function TrustBar() {
  const { stars, totalDownloads, latestVersion } = await getRepoStats()

  const items = [
    stars !== null
      ? {
          icon: Star,
          label: `${formatStars(stars)} étoiles GitHub`,
          href: site.repoUrl,
        }
      : {
          icon: GithubIcon,
          label: "Open source sur GitHub",
          href: site.repoUrl,
        },
    latestVersion
      ? { icon: Tag, label: latestVersion, href: site.releasesUrl }
      : { icon: Tag, label: "Dernière version", href: site.releasesUrl },
    totalDownloads !== null
      ? {
          icon: Download,
          label: `${formatDownloads(totalDownloads)} téléchargements`,
          href: site.releasesUrl,
        }
      : {
          icon: Download,
          label: "Téléchargements GitHub",
          href: site.releasesUrl,
        },
    { icon: ShieldCheck, label: "Licence MIT", href: site.licenseUrl },
    { icon: EyeOff, label: "Zéro télémétrie", href: undefined },
  ]

  return (
    <div className="trust-bar">
      {items.map((item, index) => {
        const content = (
          <span className="flex items-center gap-1.5">
            <item.icon className="size-3.5 text-muted-foreground/70" />
            {item.label}
          </span>
        )
        return item.href ? (
          <a
            key={index}
            href={item.href}
            className="flex items-center gap-1.5 transition-colors hover:text-foreground"
          >
            {content}
          </a>
        ) : (
          <span key={index}>{content}</span>
        )
      })}
    </div>
  )
}
