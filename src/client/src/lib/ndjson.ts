/**
 * Parse an NDJSON (Newline Delimited JSON) stream
 *
 * @param reader - ReadableStreamDefaultReader from fetch response.body
 * @returns Promise that resolves to array of parsed items
 */
export async function parseNDJSONStream<T = any>(
  reader: ReadableStreamDefaultReader<Uint8Array>
): Promise<T[]> {
  const items: T[] = []
  let buffer = ""

  while (true) {
    const { done, value } = await reader.read()
    if (done) break

    buffer += new TextDecoder().decode(value)
    const lines = buffer.split("\n")
    buffer = lines.pop() || ""

    for (const line of lines) {
      if (!line.trim()) continue
      try {
        items.push(JSON.parse(line) as T)
      } catch (e) {
        console.error("Failed to parse NDJSON line:", e, "Line:", line)
      }
    }
  }

  // Process remaining buffer
  if (buffer.trim()) {
    try {
      items.push(JSON.parse(buffer) as T)
    } catch (e) {
      console.error("Failed to parse final NDJSON line:", e, "Buffer:", buffer)
    }
  }

  return items
}

/**
 * Fetch and parse an NDJSON stream
 *
 * @param url - URL to fetch from
 * @param options - Fetch options (headers will include Accept: application/x-ndjson)
 * @returns Promise that resolves to array of parsed items
 */
export async function fetchNDJSON<T = any>(
  url: string,
  options: RequestInit = {}
): Promise<T[]> {
  const response = await fetch(url, {
    ...options,
    headers: {
      Accept: "application/x-ndjson",
      ...options.headers
    }
  })

  if (!response.ok) {
    throw new Error(`Failed to fetch: ${response.status} ${response.statusText}`)
  }

  const reader = response.body?.getReader()
  if (!reader) {
    throw new Error("No stream reader available")
  }

  return parseNDJSONStream<T>(reader)
}
