/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { type ImgHTMLAttributes, useEffect, useState } from "react"

import { useCardImage } from "@/hooks/use-card-image"
import { getApiUrl } from "@/utils/api-config"

export interface CardImageProps
  extends Omit<ImgHTMLAttributes<HTMLImageElement>, "src"> {
  catalogId: number | null
  fallback?: React.ReactNode
}

/** Renders the CDN image first, then the API's verified fallback image. */
export function CardImage({
  catalogId,
  fallback,
  onError,
  ...props
}: CardImageProps) {
  const cdnSrc = useCardImage(catalogId)
  const [source, setSource] = useState<"cdn" | "fallback" | "failed">("cdn")

  useEffect(() => {
    setSource("cdn")
  }, [catalogId])

  const fallbackSrc = catalogId != null && catalogId > 0
    ? getApiUrl(`/api/collection/cards/${catalogId}/image`)
    : null
  const imageSrc = source === "fallback" ? fallbackSrc : source === "cdn" ? cdnSrc : null

  if (!imageSrc) return <>{fallback}</>

  return (
    <img
      {...props}
      src={imageSrc}
      onError={(event) => {
        if (source === "cdn" && fallbackSrc) {
          setSource("fallback")
        } else {
          setSource("failed")
        }
        onError?.(event)
      }}
    />
  )
}
