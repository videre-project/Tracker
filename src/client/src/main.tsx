/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { StrictMode } from 'react'
import { ApplicationSettingsProvider } from './components/application-settings-provider'
import { ThemeProvider } from './components/theme-provider'
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

document.documentElement.dataset.host = navigator.userAgent.includes('VidereTracker')
  ? 'tracker'
  : 'browser'

async function resolveCardFace(catalogId: number): Promise<CardFaceInfo | null> {
  const response = await fetch(getApiUrl(`/api/collection/cards/${catalogId}/face`))
  if (!response.ok) return null

  const data = await response.json() as { catalogId?: number; name?: string }
  return typeof data.catalogId === 'number'
    ? { catalogId: data.catalogId, name: data.name }
    : null
}

async function resolveOtherFaceCatalogId(catalogId: number): Promise<number | null> {
  return (await resolveCardFace(catalogId))?.catalogId ?? null
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
    <ApplicationSettingsProvider>
      <ClientStateProvider>
        <CardArtProvider>
          <CardMediaProvider {...trackerCardMedia}>
            <SharedCardTooltipProvider resolveOtherFaceCatalogId={resolveOtherFaceCatalogId}>
              <ThemeProvider>
                <RouterProvider router={router} useTransitions={false} />
              </ThemeProvider>
            </SharedCardTooltipProvider>
          </CardMediaProvider>
        </CardArtProvider>
      </ClientStateProvider>
    </ApplicationSettingsProvider>
  </StrictMode>
)
