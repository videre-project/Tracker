/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { createContext, useContext } from "react"

export interface CardArtContextValue {
  getArtUrl: (cardName: string) => string | null
  prefetchCards: (cardNames: string[]) => Promise<void>
  isReady: boolean
}

export const CardArtContext = createContext<CardArtContextValue | null>(null)

export function useCardArtContext(): CardArtContextValue {
  const context = useContext(CardArtContext)
  if (!context) {
    throw new Error("useCardArtContext must be used within a CardArtProvider")
  }
  return context
}
