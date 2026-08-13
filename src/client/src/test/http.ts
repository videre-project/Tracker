export function jsonResponse<T>(value: T, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(value), {
    ...init,
    headers: {
      'content-type': 'application/json',
      ...init.headers,
    },
  })
}

export function ndjsonResponse<T>(
  values: readonly T[],
  init: ResponseInit = {},
): Response {
  const encoder = new TextEncoder()
  return new Response(new ReadableStream<Uint8Array>({
    start(controller) {
      for (const value of values) {
        controller.enqueue(encoder.encode(`${JSON.stringify(value)}\n`))
      }
      controller.close()
    },
  }), {
    ...init,
    headers: {
      'content-type': 'application/x-ndjson',
      ...init.headers,
    },
  })
}
