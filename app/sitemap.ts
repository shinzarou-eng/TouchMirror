import type { MetadataRoute } from "next"

export default function sitemap(): MetadataRoute.Sitemap {
  return [
    {
      url: "https://www.touchmirror.xyz/",
      lastModified: new Date("2026-09-19T00:00:00.000Z"),
      changeFrequency: "weekly",
      priority: 1,
    },
  ]
}
