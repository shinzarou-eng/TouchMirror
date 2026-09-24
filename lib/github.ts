const REPO = "shinzarou-eng/TouchMirror"

export type RepoStats = {
  stars: number | null
  totalDownloads: number | null
  latestVersion: string | null
}

function formatStars(count: number): string {
  if (count < 1000) return String(count)
  return `${(count / 1000).toFixed(1).replace(/\.0$/, "")}k`
}

export async function getRepoStats(): Promise<RepoStats> {
  try {
    const [repoRes, releaseRes] = await Promise.all([
      fetch(`https://api.github.com/repos/${REPO}`, {
        headers: { Accept: "application/vnd.github+json" },
        next: { revalidate: 3600 },
      }),
      fetch(`https://api.github.com/repos/${REPO}/releases?per_page=100`, {
        headers: { Accept: "application/vnd.github+json" },
        next: { revalidate: 3600 },
      }),
    ])

    const repo = repoRes.ok ? await repoRes.json() : null
    const releases = releaseRes.ok ? await releaseRes.json() : []
    const latestRelease = Array.isArray(releases) ? releases[0] : null
    const assets = Array.isArray(releases)
      ? releases.flatMap((release) => (Array.isArray(release?.assets) ? release.assets : []))
      : []
    const totalDownloads = assets.reduce(
      (total, asset) => total + (typeof asset?.download_count === "number" ? asset.download_count : 0),
      0,
    )

    const stars = typeof repo?.stargazers_count === "number" ? repo.stargazers_count : null

    return {
      stars,
      totalDownloads: assets.length > 0 ? totalDownloads : null,
      latestVersion: typeof latestRelease?.tag_name === "string" ? latestRelease.tag_name : null,
    }
  } catch {
    return { stars: null, totalDownloads: null, latestVersion: null }
  }
}

export function formatDownloads(count: number): string {
  return new Intl.NumberFormat("fr-FR").format(count)
}

export { formatStars }
