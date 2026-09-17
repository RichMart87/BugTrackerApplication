/**
 * Thin wrapper over the BugTracker API.
 *
 * In development the Vite proxy forwards /api to the .NET host, so the browser
 * stays same-origin. Set VITE_API_BASE_URL to call an absolute backend instead.
 */
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api'

export const SEVERITIES = ['Critical', 'High', 'Medium', 'Low']
export const ENVIRONMENTS = ['Local', 'QA', 'Staging', 'Production']

/** Raised for non-2xx responses; carries field errors from ASP.NET ProblemDetails. */
export class ApiError extends Error {
  constructor(message, { status = 0, fieldErrors = {} } = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

/**
 * Builds a query string, repeating array values (severity=Critical&severity=High)
 * which is the shape ASP.NET model binding expects, and dropping empty values.
 */
function toQueryString(params = {}) {
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue

    if (Array.isArray(value)) {
      value.filter((v) => v !== null && v !== undefined && v !== '').forEach((v) => search.append(key, v))
    } else {
      search.append(key, value)
    }
  }

  const query = search.toString()
  return query ? `?${query}` : ''
}

async function request(path, options = {}) {
  let response

  try {
    response = await fetch(`${BASE_URL}${path}`, {
      headers: options.body ? { 'Content-Type': 'application/json' } : undefined,
      ...options
    })
  } catch {
    throw new ApiError('Could not reach the server. Is the API running?', { status: 0 })
  }

  if (response.status === 204) return null

  if (!response.ok) {
    throw await toApiError(response)
  }

  return response.json()
}

async function toApiError(response) {
  let payload = null

  try {
    payload = await response.json()
  } catch {
    // Non-JSON error body - fall through to the status-based message.
  }

  const fieldErrors = {}

  if (payload?.errors) {
    for (const [key, messages] of Object.entries(payload.errors)) {
      // ProblemDetails keys arrive as "DateFound" or "$.severity"; normalise to camelCase.
      const field = key.replace(/^\$\./, '')
      fieldErrors[field.charAt(0).toLowerCase() + field.slice(1)] = Array.isArray(messages)
        ? messages.join(' ')
        : String(messages)
    }
  }

  // detail carries the actionable sentence (e.g. "narrow the filters"); title is the headline.
  const message =
    Object.values(fieldErrors)[0] ||
    payload?.detail ||
    payload?.title ||
    (response.status === 404 ? 'That bug no longer exists.' : `Request failed (${response.status}).`)

  return new ApiError(message, { status: response.status, fieldErrors })
}

export const bugApi = {
  list: (params) => request(`/bugs${toQueryString(params)}`),
  get: (id) => request(`/bugs/${id}`),
  create: (bug) => request('/bugs', { method: 'POST', body: JSON.stringify(bug) }),
  update: (id, bug) => request(`/bugs/${id}`, { method: 'PUT', body: JSON.stringify(bug) }),
  setResolved: (id, resolved) =>
    request(`/bugs/${id}/resolved`, { method: 'PATCH', body: JSON.stringify({ resolved }) }),
  remove: (id) => request(`/bugs/${id}`, { method: 'DELETE' }),
  summary: () => request('/bugs/stats/summary'),
  monthlyTrend: (months = 12) => request(`/bugs/stats/monthly${toQueryString({ months })}`),

  /**
   * Streams an export to a file. Downloading through fetch (rather than pointing
   * window.location at the URL) keeps the current filters and lets errors surface
   * in the UI instead of replacing the page with a server error.
   */
  async download(format, params) {
    const response = await fetch(`${BASE_URL}/bugs/export/${format}${toQueryString(params)}`)

    if (!response.ok) {
      throw await toApiError(response)
    }

    const blob = await response.blob()
    const disposition = response.headers.get('Content-Disposition') ?? ''
    const match = /filename="?([^";]+)"?/i.exec(disposition)
    const filename = match?.[1] ?? `bug-report.${format}`

    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = filename
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)

    return filename
  }
}
