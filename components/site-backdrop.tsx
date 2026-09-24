"use client"

import { useEffect, useRef } from "react"

const COLORS = [
  [120, 220, 175],
  [80, 200, 165],
  [110, 160, 210],
]

export function SiteBackdrop() {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext("2d")
    if (!ctx) return

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches
    const sprites = COLORS.map(([r, g, b]) => {
      const c = document.createElement("canvas")
      c.width = c.height = 64
      const s = c.getContext("2d")
      if (!s) return c
      const grad = s.createRadialGradient(32, 32, 0, 32, 32, 32)
      grad.addColorStop(0, `rgba(${r},${g},${b},0.9)`)
      grad.addColorStop(0.3, `rgba(${r},${g},${b},0.32)`)
      grad.addColorStop(1, `rgba(${r},${g},${b},0)`)
      s.fillStyle = grad
      s.fillRect(0, 0, 64, 64)
      return c
    })

    type P = {
      x: number
      y: number
      vx: number
      vy: number
      r: number
      a: number
      tw: number
      sprite: number
    }
    let parts: P[] = []
    let w = 0
    let h = 0
    const mouse = { x: -9999, y: -9999 }

    const resize = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2)
      w = window.innerWidth
      h = window.innerHeight
      canvas.width = Math.round(w * dpr)
      canvas.height = Math.round(h * dpr)
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      const n = Math.min(150, Math.max(50, Math.round((w * h) / 15000)))
      parts = Array.from({ length: n }, () => ({
        x: Math.random() * w,
        y: Math.random() * h,
        vx: (Math.random() - 0.5) * 0.18,
        vy: (Math.random() - 0.5) * 0.18,
        r: 7 + Math.random() * 16,
        a: 0.1 + Math.random() * 0.28,
        tw: Math.random() * Math.PI * 2,
        sprite: Math.random() < 0.62 ? 0 : Math.random() < 0.7 ? 1 : 2,
      }))
    }

    const onMove = (e: PointerEvent) => {
      mouse.x = e.clientX
      mouse.y = e.clientY
    }
    const onLeave = () => {
      mouse.x = -9999
      mouse.y = -9999
    }

    const RADIUS = 170
    let t = 0
    let raf = 0

    const draw = () => {
      t += 0.016
      ctx.clearRect(0, 0, w, h)
      for (const p of parts) {
        p.x += p.vx
        p.y += p.vy
        const dx = mouse.x - p.x
        const dy = mouse.y - p.y
        const d = Math.hypot(dx, dy)
        let alpha = p.a * (0.72 + 0.28 * Math.sin(t * 0.9 + p.tw))
        if (d < RADIUS && d > 0.1) {
          const f = 1 - d / RADIUS
          p.x += (dx / d) * f * 1.4
          p.y += (dy / d) * f * 1.4
          alpha = Math.min(0.95, alpha + f * 0.55)
        }
        if (p.x < -40) p.x = w + 40
        else if (p.x > w + 40) p.x = -40
        if (p.y < -40) p.y = h + 40
        else if (p.y > h + 40) p.y = -40
        ctx.globalAlpha = alpha
        const s = p.r * 2
        ctx.drawImage(sprites[p.sprite], p.x - p.r, p.y - p.r, s, s)
      }
      ctx.globalAlpha = 1
    }

    const loop = () => {
      draw()
      raf = requestAnimationFrame(loop)
    }

    resize()
    if (reduced) {
      draw()
    } else {
      raf = requestAnimationFrame(loop)
      window.addEventListener("pointermove", onMove, { passive: true })
      document.documentElement.addEventListener("pointerleave", onLeave)
    }
    window.addEventListener("resize", resize)
    const onVis = () => {
      if (document.hidden) cancelAnimationFrame(raf)
      else if (!reduced) raf = requestAnimationFrame(loop)
    }
    document.addEventListener("visibilitychange", onVis)

    return () => {
      cancelAnimationFrame(raf)
      window.removeEventListener("resize", resize)
      window.removeEventListener("pointermove", onMove)
      document.documentElement.removeEventListener("pointerleave", onLeave)
      document.removeEventListener("visibilitychange", onVis)
    }
  }, [])

  return (
    <div className="site-bg" aria-hidden="true">
      <canvas ref={canvasRef} className="site-bg-dust" />
    </div>
  )
}
