import { ReleaseBanner } from "@/components/release-banner"
import { SiteBackdrop } from "@/components/site-backdrop"
import { SiteHeader } from "@/components/site-header"
import { Hero } from "@/components/hero"
import { TesterCta } from "@/components/tester-cta"
import { ScreenshotShowcase } from "@/components/screenshot-showcase"
import { FeaturesGrid } from "@/components/features-grid"
import { HowItWorks } from "@/components/how-it-works"
import { ExtrasTable } from "@/components/extras-table"
import { SiteFooter } from "@/components/site-footer"

const siteUrl = "https://www.touchmirror.xyz"

const structuredData = {
  "@context": "https://schema.org",
  "@type": "SoftwareApplication",
  name: "TouchMirror",
  applicationCategory: "GameApplication",
  operatingSystem: "Windows",
  description:
    "Mirroring Android natif et contrôle d'un vrai téléphone depuis Windows, pensé pour Dofus Touch.",
  url: siteUrl,
  downloadUrl: "https://github.com/shinzarou-eng/TouchMirror/releases/latest",
  codeRepository: "https://github.com/shinzarou-eng/TouchMirror",
  license: "https://github.com/shinzarou-eng/TouchMirror/blob/main/LICENSE",
  isAccessibleForFree: true,
}

export default function Home() {
  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }}
      />
      <SiteBackdrop />
      <ReleaseBanner />
      <SiteHeader />
      <main id="main" tabIndex={-1}>
        <Hero />
        <ScreenshotShowcase />
        <FeaturesGrid />
        <HowItWorks />
        <ExtrasTable />
        <TesterCta />
      </main>
      <SiteFooter />
    </>
  )
}
