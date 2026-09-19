import type { Metadata, Viewport } from "next"
import { Geist, Geist_Mono } from "next/font/google"

import "./globals.css"
import { cn } from "@/lib/utils"

const geist = Geist({ subsets: ["latin"], variable: "--font-sans" })

const fontMono = Geist_Mono({
  subsets: ["latin"],
  variable: "--font-mono",
})

const siteUrl = "https://www.touchmirror.xyz"

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  title: {
    default: "Dofus Touch Mirroring sur PC — TouchMirror",
    template: "%s · TouchMirror",
  },
  description:
    "Dofus Touch mirroring sur PC Windows : affiche et contrôle ton vrai téléphone Android avec TouchMirror. Gratuit, open source, sans émulateur ni automatisation.",
  keywords: [
    "dofus touch mirroring",
    "dofus touch miroir pc",
    "dofus touch pc",
    "mirroring dofus touch",
    "android mirroring windows",
    "mirror android to pc",
    "scrcpy alternative",
    "dofus touch multicompte",
  ],
  authors: [{ name: "TouchMirror" }],
  alternates: { canonical: siteUrl },
  robots: {
    index: true,
    follow: true,
    googleBot: {
      index: true,
      follow: true,
      "max-image-preview": "large",
      "max-snippet": -1,
    },
  },
  openGraph: {
    type: "website",
    url: siteUrl,
    title: "TouchMirror — Joue à Dofus Touch sur PC depuis ton vrai téléphone",
    description:
      "Mirroring Android natif, open source et gratuit. Pas un émulateur, pas de macro — chaque action vient de toi.",
    siteName: "TouchMirror",
    images: [{ url: "/images/screenshot.png", width: 1280, height: 800 }],
  },
  twitter: {
    card: "summary_large_image",
    title: "TouchMirror — Android mirroring for Dofus Touch",
    description: "Mirror & control your real Android phone on PC. Open source, no emulator, no automation.",
    images: ["/images/screenshot.png"],
  },
  icons: {
    icon: [
      { url: "/icon.png", type: "image/png" },
      { url: "/images/mascot.png", type: "image/png" },
    ],
    shortcut: "/icon.png",
    apple: "/icon.png",
  },
}

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#0a0b0f",
  colorScheme: "dark",
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <html
      lang="fr"
      className={cn("dark antialiased bg-background", fontMono.variable, "font-sans", geist.variable)}
    >
      <body className="bg-background text-foreground">{children}</body>
    </html>
  )
}
