/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, type ReactNode } from "react"
import { useApplicationSettings } from "@/hooks/use-application-settings"

export function ThemeProvider({ children, storageKey = "vite-ui-theme" }: {
  children: ReactNode
  storageKey?: string
}) {
  const { preferences: { theme } } = useApplicationSettings()

  useEffect(() => {
    localStorage.setItem(storageKey, theme)
    const media = window.matchMedia("(prefers-color-scheme: dark)")
    const apply = () => {
      const root = document.documentElement
      root.classList.remove("light", "dark")
      root.classList.add(theme === "system" ? media.matches ? "dark" : "light" : theme)
      const host = window as Window & { chrome?: { webview?: { postMessage: (message: string) => void } } }
      host.chrome?.webview?.postMessage("tracker-theme-changed")
    }
    apply()
    if (theme === "system") media.addEventListener("change", apply)
    return () => media.removeEventListener("change", apply)
  }, [theme, storageKey])

  return <>{children}</>
}
