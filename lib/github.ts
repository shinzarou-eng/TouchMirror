const REPO = "shinzarou-eng/TouchMirror"

export type RepoStats = {
  stars: number | null
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
      fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
        headers: { Accept: "application/vnd.github+json" },
        next: { revalidate: 3600 },
      }),
    ])

    const repo = repoRes.ok ? await repoRes.json() : null
    const release = releaseRes.ok ? await releaseRes.json() : null

    const stars = typeof repo?.stargazers_count === "number" ? repo.stargazers_count : null

    return {
      stars,
      latestVersion: typeof release?.tag_name === "string" ? release.tag_name : null,
    }
  } catch {
    return { stars: null, latestVersion: null }
  }
}

export { formatStars }
