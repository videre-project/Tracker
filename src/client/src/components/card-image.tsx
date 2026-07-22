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
  fallback?: React.ReactNode
}

/**
 * Renders a card image with zero blank/broken-icon frames.
 * 
 * We do this by tracking the last uRL that was fully decoded and safe to paint,
 * and only swapping to a new URL once it has been decoded off-screen.
 * 
 * This prevents the native browser broken-image icon from appearing when a CDN
 * URL fails to load, and also prevents a blank frame from appearing while a new
 * image is being decoded.
 */
export function CardImage({
  catalogId,
  fallback,
  onError,
  style,
  className,
  ...props
}: CardImageProps) {
  const cdnSrc = getCardImageSync(catalogId)
  const fallbackSrc = catalogId != null && catalogId > 0
    ? getApiUrl(`/api/collection/cards/${catalogId}/image`)
    : null

  // The best URL to try first for this catalogId (skips CDN if known-failed)
  const targetSrc = getBestImageUrl(catalogId, fallbackSrc)

  // The URL currently painted on screen. null = nothing committed yet.
  // Initialize synchronously: if this URL is already decoded, show it immediately.
  const [committedSrc, setCommittedSrc] = useState<string | null>(() =>
    targetSrc && isUrlDecoded(targetSrc) ? targetSrc : null
  )

  // Track what catalogId we computed committedSrc for (for synchronous reset)
  const [prevCatalogId, setPrevCatalogId] = useState(catalogId)

  // Synchronous catalogId-change handler (React docs pattern for derived state)
  if (catalogId !== prevCatalogId) {
    setPrevCatalogId(catalogId)
    const newTarget = getBestImageUrl(catalogId, fallbackSrc)
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
