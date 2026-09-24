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
      <p className="flex items-center gap-2 font-mono text-xs font-medium tracking-[0.14em] text-muted-foreground uppercase">
        <span className="size-1.5 rounded-full bg-primary" aria-hidden="true" />
        {kicker}
      </p>
      <h2 className="mt-2 text-2xl font-semibold tracking-tight text-balance sm:text-3xl">{title}</h2>
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
