import { useState, useEffect } from "react"

interface CardArtProps {
  cardName: string
  className?: string
}

export function CardArt({ cardName, className = "" }: CardArtProps) {
  const [artUrl, setArtUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const fetchCardArt = async () => {
      try {
        const response = await fetch(
          `https://api.scryfall.com/cards/named?fuzzy=${encodeURIComponent(cardName)}`
        )

        if (!response.ok) {
          throw new Error("Card not found")
        }

        const data = await response.json()
        setArtUrl(data.image_uris?.art_crop || null)
      } catch (error) {
        console.error(`Failed to fetch art for ${cardName}:`, error)
        setArtUrl(null)
      } finally {
        setLoading(false)
      }
    }

    fetchCardArt()
  }, [cardName])

  if (loading) {
    return (
      <div className={`bg-muted/50 animate-pulse ${className}`} />
    )
  }

  if (!artUrl) {
    return (
      <div className={`flex items-center justify-center bg-muted/50 text-xs font-medium text-muted-foreground ${className}`}>
        ?
      </div>
    )
  }

  return (
    <img
      src={artUrl}
      alt={cardName}
      className={`object-cover ${className}`}
    />
  )
}
