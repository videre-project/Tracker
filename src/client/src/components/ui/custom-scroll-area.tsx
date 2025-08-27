import * as React from "react"
import { cn } from "@/lib/utils"

interface CustomScrollAreaProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode
  maxHeight?: string
  showScrollbar?: boolean
  variant?: "default" | "minimal" | "thick"
}

const CustomScrollArea = React.forwardRef<HTMLDivElement, CustomScrollAreaProps>(
  ({ children, className, maxHeight = "100%", showScrollbar = true, variant = "default", ...props }, ref) => {
    const scrollbarClasses = {
      default: "scrollbar-webkit scrollbar-thin",
      minimal: "scrollbar-webkit [&::-webkit-scrollbar]:w-1 [&::-webkit-scrollbar-thumb]:bg-muted-foreground/30",
      thick: "scrollbar-webkit [&::-webkit-scrollbar]:w-3"
    }

    return (
      <div
        ref={ref}
        className={cn(
          "overflow-auto",
          showScrollbar ? scrollbarClasses[variant] : "scrollbar-none",
          className
        )}
        style={{ maxHeight }}
        {...props}
      >
        {children}
      </div>
    )
  }
)

CustomScrollArea.displayName = "CustomScrollArea"

export { CustomScrollArea }
