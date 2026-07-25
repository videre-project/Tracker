/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { type ImgHTMLAttributes, useEffect, useRef, useState } from "react"

import {
  getCardImageSync,
  getBestImageUrl,
  isUrlDecoded,
  isUrlFailed,
  markUrlDecoded,
  markUrlFailed,
} from "@/utils/card-image-cache"
import { getApiUrl } from "@/utils/api-config"

export interface CardImageProps
  extends Omit<ImgHTMLAttributes<HTMLImageElement>, "src"> {
  catalogId: number | null
  textureId?: number | null
  name?: string
  initialName?: string
  fallback?: React.ReactNode
}

interface BackFaceInfo {
  catalogId: number
  name: string
}

const s_faceCatalogIdCache = new Map<number, BackFaceInfo | null>()

/**
 * Renders a card image with zero blank/broken-icon frames.
 * 
 * We do this by tracking the last URL that was fully decoded and safe to paint,
 * and only swapping to a new URL once it has been decoded off-screen.
 * 
 * This prevents the native browser broken-image icon from appearing when a CDN
 * URL fails to load, and also prevents a blank frame from appearing while a new
 * image is being decoded.
 */
export function CardImage({
  catalogId,
  textureId,
  name,
  initialName,
  fallback,
  onError,
  style,
  className,
  ...props
}: CardImageProps) {
  const [backFaceInfo, setBackFaceInfo] = useState<BackFaceInfo | null>(() => {
    if (!catalogId) return null
    return s_faceCatalogIdCache.get(catalogId) ?? null
  })

  useEffect(() => {
    if (!catalogId) return
    if (s_faceCatalogIdCache.has(catalogId)) {
      setBackFaceInfo(s_faceCatalogIdCache.get(catalogId) ?? null)
      return
    }

    let isMounted = true
    fetch(getApiUrl(`/api/collection/cards/${catalogId}/face`))
      .then(res => (res.ok ? res.json() : null))
      .then(data => {
        if (!isMounted) return
        const info: BackFaceInfo | null =
          data?.catalogId != null
            ? { catalogId: data.catalogId as number, name: (data.name as string) ?? "" }
            : null
        s_faceCatalogIdCache.set(catalogId, info)
        setBackFaceInfo(info)
      })
      .catch(() => {
        if (!isMounted) return
        s_faceCatalogIdCache.set(catalogId, null)
      })

    return () => {
      isMounted = false
    }
  }, [catalogId])

  // A card uses the resolved back-face catalog ID if its current name matches the back face name
  const isMatchingBackFace = Boolean(
    name &&
      backFaceInfo?.name &&
      name.trim().toLowerCase() === backFaceInfo.name.trim().toLowerCase()
  )
  const activeCatalogId = isMatchingBackFace ? backFaceInfo!.catalogId : catalogId

  const textureSrc = textureId != null && textureId > 0
    ? getApiUrl(`/api/collection/cards/texture/${textureId}/image`)
    : null

  const fallbackSrc = activeCatalogId != null && activeCatalogId > 0
    ? getApiUrl(`/api/collection/cards/${activeCatalogId}/image`)
    : name
    ? getApiUrl(`/api/collection/cards/${encodeURIComponent(name)}/image`)
    : null

  // Prioritize activeCatalogId CDN for exact printing; fallback to textureSrc if CDN fails.
  const targetSrc = getBestImageUrl(activeCatalogId, textureSrc ?? fallbackSrc)

  // The URL currently painted on screen. null = nothing committed yet.
  // Initialize synchronously: if this URL is already decoded, show it immediately.
  const [committedSrc, setCommittedSrc] = useState<string | null>(() =>
    targetSrc && isUrlDecoded(targetSrc) ? targetSrc : null
  )

  // Track what catalogId, textureId, and name we computed committedSrc for (for synchronous reset)
  const [prevCatalogId, setPrevCatalogId] = useState(activeCatalogId)
  const [prevTextureId, setPrevTextureId] = useState(textureId)
  const [prevName, setPrevName] = useState(name)

  // Synchronous catalogId/textureId/name-change handler (React docs pattern for derived state)
  if (activeCatalogId !== prevCatalogId || textureId !== prevTextureId || name !== prevName) {
    setPrevCatalogId(activeCatalogId)
    setPrevTextureId(textureId)
    setPrevName(name)
    const newTarget = getBestImageUrl(activeCatalogId, textureSrc ?? fallbackSrc)
    // If already decoded, commit immediately — zero blank frame
    setCommittedSrc(newTarget && isUrlDecoded(newTarget) ? newTarget : null)
  }

  // Track the URL we are currently decoding so we can cancel stale loads
  const pendingTarget = useRef<string | null>(null)

  useEffect(() => {
    if (!targetSrc) {
      setCommittedSrc(null)
      return
    }

    // Already committed (was decoded on init or synchronous reset above)
    if (committedSrc === targetSrc) return

    // Already decoded globally — commit and return without spawning a new Image
    if (isUrlDecoded(targetSrc)) {
      setCommittedSrc(targetSrc)
      return
    }

    // Kick off off-screen decode
    pendingTarget.current = targetSrc

    async function tryLoad(url: string, isFallback = false) {
      const img = new Image()
      img.src = url
      try {
        await img.decode()
        if (pendingTarget.current !== targetSrc) return // stale
        markUrlDecoded(url)
        setCommittedSrc(url)
      } catch {
        if (pendingTarget.current !== targetSrc) return // stale
        if (!isFallback && fallbackSrc && !isUrlFailed(url)) {
          markUrlFailed(url)
          tryLoad(fallbackSrc, true)
        } else {
          setCommittedSrc(null)
        }
      }
    }

    tryLoad(targetSrc, targetSrc === fallbackSrc)

    return () => {
      pendingTarget.current = null
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [targetSrc])

  // Nothing committed yet and no fallback UI provided — render nothing.
  // Crucially we do NOT render a broken <img> here, so no native broken icon.
  if (!committedSrc) return <>{fallback}</>

  return (
    <img
      {...props}
      src={committedSrc}
      className={className}
      style={style}
      onError={(event) => {
        // This fires if the committed URL becomes invalid after it was decoded
        // (e.g. CDN URL expires). Try the fallback immediately.
        if (committedSrc === cdnSrc && fallbackSrc && !isUrlFailed(committedSrc)) {
          markUrlFailed(committedSrc)
          if (isUrlDecoded(fallbackSrc)) {
            setCommittedSrc(fallbackSrc)
          } else {
            const fb = new Image()
            fb.src = fallbackSrc
            fb.decode()
              .then(() => { markUrlDecoded(fallbackSrc); setCommittedSrc(fallbackSrc) })
              .catch(() => setCommittedSrc(null))
          }
        } else {
          setCommittedSrc(null)
        }
        onError?.(event)
      }}
    />
  )
}
