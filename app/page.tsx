import { SiteHeader } from "@/components/site-header"
import { Hero } from "@/components/hero"
import { TesterCta } from "@/components/tester-cta"
import { ScreenshotShowcase } from "@/components/screenshot-showcase"
import { FeaturesGrid } from "@/components/features-grid"
import { HowItWorks } from "@/components/how-it-works"
import { ComparisonTable } from "@/components/comparison-table"
import { ExtrasTable } from "@/components/extras-table"
import { SiteFooter } from "@/components/site-footer"

export default function Home() {
  return (
    <>
      <SiteHeader />
      <main>
        <Hero />
        <TesterCta />
        <ScreenshotShowcase />
        <FeaturesGrid />
        <HowItWorks />
        <ComparisonTable />
        <ExtrasTable />
      </main>
      <SiteFooter />
    </>
  )
}
