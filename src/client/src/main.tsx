/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router-dom';
import router from './router.tsx';
import { CardArtProvider } from './components/card-art';
import { ClientStateProvider } from './components/client-state-provider';
import {
  CardMediaProvider,
  CardTooltipProvider as SharedCardTooltipProvider,
  type CardFaceInfo,
  type CardMediaServices,
} from '@videreproject/ui'
import { getApiUrl } from './utils/api-config.ts'
import '@videreproject/ui/theme.css'
import './index.css'

async function resolveCardFace(catalogId: number): Promise<CardFaceInfo | null> {
  const response = await fetch(getApiUrl(`/api/collection/cards/${catalogId}/face`))
  if (!response.ok) return null

  const data = await response.json() as { catalogId?: number; name?: string }
  return typeof data.catalogId === 'number'
    ? { catalogId: data.catalogId, name: data.name }
    : null
}

const trackerCardMedia: CardMediaServices = {
  getCardImageFallbackUrl: catalogId =>
    getApiUrl(`/api/collection/cards/${catalogId}/image`),
  getCardTextureImageUrl: textureId =>
    getApiUrl(`/api/collection/cards/texture/${textureId}/image`),
  getNamedCardImageUrl: name =>
    getApiUrl(`/api/collection/cards/${encodeURIComponent(name)}/image`),
  getCardArtUrl: catalogId =>
    getApiUrl(`/api/collection/cards/${catalogId}/art`),
  resolveCardFace,
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ClientStateProvider>
      <CardArtProvider>
        <CardMediaProvider {...trackerCardMedia}>
          <SharedCardTooltipProvider>
            <RouterProvider router={router} />
          </SharedCardTooltipProvider>
        </CardMediaProvider>
      </CardArtProvider>
    </ClientStateProvider>
  </StrictMode>
)
