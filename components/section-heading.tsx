import { cn } from "@/lib/utils"

export function SectionHeading({
  kicker,
  title,
  description,
  align = "left",
  className,
}: {
  kicker: string
  title: string
  description?: string
  align?: "left" | "center"
  className?: string
}) {
  return (
    <div className={cn(align === "center" && "text-center", className)}>
      <div
        className={cn(
          "flex items-center gap-2 font-mono text-xs font-medium tracking-[0.18em] text-primary uppercase",
          align === "center" && "justify-center",
        )}
      >
        <span className="h-px w-4 bg-primary/50" aria-hidden="true" />
        {kicker}
      </div>
      <h2 className="mt-3 text-2xl font-semibold tracking-tight text-balance sm:text-3xl">{title}</h2>
      {description && (
        <p
          className={cn(
            "mt-2 max-w-xl text-sm text-muted-foreground text-pretty",
            align === "center" && "mx-auto",
          )}
        >
          {description}
        </p>
      )}
    </div>
  )
}
