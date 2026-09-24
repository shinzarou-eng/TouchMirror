import { ArrowRight, Sparkles } from "lucide-react"
import { site } from "@/lib/site"

type Release = {
  tag_name?: string
  html_url?: string
}

async function getLatestRelease(): Promise<Release | null> {
  try {
    const res = await fetch(
      "https://api.github.com/repos/shinzarou-eng/TouchMirror/releases/latest",
      {
        next: { revalidate: 3600 },
        headers: {
          Accept: "application/vnd.github+json",
          "User-Agent": "touchmirror-site",
        },
      },
    )
    if (!res.ok) return null
    return (await res.json()) as Release
  } catch {
    return null
  }
}

export async function ReleaseBanner() {
  const release = await getLatestRelease()
  if (!release?.tag_name) return null
  return (
    <a
      className="release-banner"
      href={release.html_url ?? site.releasesUrl}
      target="_blank"
      rel="noreferrer"
    >
      <Sparkles size={13} aria-hidden="true" />
      <span>
        <strong>{release.tag_name}</strong> est sortie — voir les nouveautés
      </span>
      <ArrowRight size={13} aria-hidden="true" />
    </a>
  )
}
