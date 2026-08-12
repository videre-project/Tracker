/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { Config } from "tailwindcss"
import animate from "tailwindcss-animate"
import viderePreset from "@videreproject/ui/tailwind-preset"

const config: Config = {
  presets: [viderePreset],
  darkMode: ["class"],
  content: [
    "./index.html",
    "./src/**/*.{ts,tsx,js,jsx,mdx}",
    "./node_modules/@videreproject/ui/dist/**/*.{js,mjs}",
  ],
  plugins: [animate],
}
export default config
