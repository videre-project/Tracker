/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { Config } from "tailwindcss"
import animate from "tailwindcss-animate"
import loadConfig from "tailwindcss/loadConfig"
import fs from "node:fs"
import path from "node:path"
import sourcePaths from "./tsconfig.paths.json"

const localPreset = path.resolve(__dirname, sourcePaths.compilerOptions.paths["@videreproject/ui/tailwind-preset"][0])
const viderePreset = loadConfig(fs.existsSync(localPreset)
  ? localPreset
  : require.resolve("@videreproject/ui/tailwind-preset"))

const config: Config = {
  presets: [viderePreset],
  darkMode: ["class"],
  content: [
    "./index.html",
    "./src/**/*.{ts,tsx,js,jsx,mdx}",
    "./node_modules/@videreproject/ui/dist/**/*.{js,mjs}",
    "../../../platform/packages/ui/src/**/*.{ts,tsx}",
    "/platform/packages/ui/src/**/*.{ts,tsx}",
  ],
  plugins: [animate],
}
export default config
