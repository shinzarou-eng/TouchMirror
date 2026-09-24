"use client"

import { useEffect, useRef } from "react"
import { useReducedMotion } from "motion/react"

export function HeroVideo() {
  const reduceMotion = useReducedMotion()
  const ref = useRef<HTMLVideoElement>(null)

  useEffect(() => {
    const video = ref.current
    if (!video) return
    if (reduceMotion) {
      video.pause()
    } else {
      video.play().catch(() => {})
    }
  }, [reduceMotion])

  return (
    <video
      ref={ref}
      className="hero-video"
      src="/videos/touchmirror-usage.mp4"
      poster="/images/screenshot.png"
      autoPlay
      muted
      loop
      playsInline
      preload="metadata"
      aria-label="Une partie de Dofus Touch affichée en direct dans TouchMirror, le personnage se déplace au clic"
    />
  )
}
