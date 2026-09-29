const DEFAULT_SERVER_URL = window.__CRAWLSHARP_CONFIG__?.CRAWLSHARP_SERVER_URL ??
  localStorage.getItem('crawlsharp_server_url') ??
  'http://localhost:8000'

export function getServerUrl() {
  return DEFAULT_SERVER_URL
}

function parseIntSetting(value, fallback) {
  const parsed = Number.parseInt(value, 10)
  return Number.isNaN(parsed) ? fallback : parsed
}

export async function checkServerHealth(serverUrl) {
  try {
    const url = serverUrl || '/healthz'
    const resp = await fetch(url, { method: 'HEAD', signal: AbortSignal.timeout(5000) })
    return resp.ok
  } catch {
    return false
  }
}

export async function startCrawl(serverUrl, settings, onResource, onDone, onError, signal) {
  try {
    const resp = await fetch(`${serverUrl}/crawl`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
      signal
    })

    if (!resp.ok) {
      const text = await resp.text()
      onError(new Error(`Server returned ${resp.status}: ${text}`))
      return
    }

    const reader = resp.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop()

      for (const line of lines) {
        const trimmed = line.trim()
        if (!trimmed) continue

        if (trimmed.startsWith('data:')) {
          const data = trimmed.slice(5).trim()
          if (data === '[DONE]') {
            onDone()
            return
          }
          try {
            const resource = JSON.parse(data)
            onResource(resource)
          } catch {
            // skip malformed JSON
          }
        }
      }
    }

    onDone()
  } catch (err) {
    if (err.name !== 'AbortError') {
      onError(err)
    }
  }
}

export function formatBytes(bytes) {
  if (bytes == null || bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i]
}

export function formatDuration(ms) {
  if (ms < 1000) return ms + 'ms'
  if (ms < 60000) return (ms / 1000).toFixed(1) + 's'
  const mins = Math.floor(ms / 60000)
  const secs = Math.floor((ms % 60000) / 1000)
  return `${mins}m ${secs}s`
}

export function getStatusClass(code) {
  if (code >= 200 && code < 300) return 's2xx'
  if (code >= 300 && code < 400) return 's3xx'
  if (code >= 400 && code < 500) return 's4xx'
  return 's5xx'
}

export function classifyContentType(ct) {
  if (!ct) return 'Other'
  ct = ct.toLowerCase()
  if (ct.includes('text/html') || ct.includes('xhtml')) return 'HTML'
  if (ct.includes('text/css')) return 'CSS'
  if (ct.includes('javascript') || ct.includes('ecmascript')) return 'JavaScript'
  if (ct.includes('image/')) return 'Image'
  if (ct.includes('application/json')) return 'JSON'
  if (ct.includes('application/xml') || ct.includes('text/xml')) return 'XML'
  if (ct.includes('application/pdf')) return 'PDF'
  if (ct.includes('font/') || ct.includes('application/font')) return 'Font'
  return 'Other'
}

export function buildSettingsPayload(config) {
  const settings = {
    Crawl: {
      UserAgent: config.userAgent || 'CrawlSharp',
      StartUrl: config.startUrl,
      UseHeadlessBrowser: config.useHeadlessBrowser || false,
      IgnoreRobotsText: config.ignoreRobotsText || false,
      IncludeSitemap: config.includeSitemap !== false,
      FollowLinks: config.followLinks !== false,
      FollowRedirects: config.followRedirects !== false,
      MaxRedirects: parseIntSetting(config.maxRedirects, 10),
      RestrictToChildUrls: config.restrictToChildUrls !== false,
      RestrictToSameSubdomain: config.restrictToSameSubdomain !== false,
      RestrictToSameRootDomain: config.restrictToSameRootDomain !== false,
      MaxCrawlDepth: parseIntSetting(config.maxCrawlDepth, 5),
      FollowExternalLinks: config.followExternalLinks !== false,
      MaxParallelTasks: parseIntSetting(config.maxParallelTasks, 8),
      PageTimeoutMs: parseIntSetting(config.pageTimeoutMs, 30000),
      ThrottleMs: parseIntSetting(config.throttleMs, 5000),
      RetryOn429: config.retryOn429 !== false,
      MaxRetries: parseIntSetting(config.maxRetries, 3),
      RetryMinBackoffMs: parseIntSetting(config.retryMinBackoffMs, 1000),
      RetryMaxBackoffMs: parseIntSetting(config.retryMaxBackoffMs, 30000),
      RetryBackoffJitter: config.retryBackoffJitter !== false,
      RequestDelayMs: parseIntSetting(config.requestDelayMs, 2500),
      AutoExpandCollapsibles: config.autoExpandCollapsibles === true,
      PostLoadDelayMs: parseIntSetting(config.postLoadDelayMs, 0),
      PostInteractionDelayMs: parseIntSetting(config.postInteractionDelayMs, 250),
      MaxExpansionPasses: parseIntSetting(config.maxExpansionPasses, 2)
    }
  }

  if (config.allowedDomains && config.allowedDomains.trim()) {
    settings.Crawl.AllowedDomains = config.allowedDomains.split('\n').map(d => d.trim()).filter(Boolean)
  }

  if (config.deniedDomains && config.deniedDomains.trim()) {
    settings.Crawl.DeniedDomains = config.deniedDomains.split('\n').map(d => d.trim()).filter(Boolean)
  }

  if (config.expansionSelectors && config.expansionSelectors.trim()) {
    settings.Crawl.ExpansionSelectors = config.expansionSelectors.split('\n').map(s => s.trim()).filter(Boolean)
  }

  if (config.authType && config.authType !== 'None') {
    settings.Authentication = { Type: config.authType }
    if (config.authType === 'Basic') {
      settings.Authentication.Username = config.authUsername || ''
      settings.Authentication.Password = config.authPassword || ''
    } else if (config.authType === 'ApiKey') {
      settings.Authentication.ApiKeyHeader = config.authApiKeyHeader || ''
      settings.Authentication.ApiKey = config.authApiKey || ''
    } else if (config.authType === 'BearerToken') {
      settings.Authentication.BearerToken = config.authBearerToken || ''
    }

    const origins = splitLines(config.authCredentialOrigins)
    if (origins.length > 0) settings.Authentication.CredentialOrigins = origins
  }

  return settings
}

function splitLines(value) {
  if (!value || !value.trim()) return []
  return value.split('\n').map(v => v.trim()).filter(Boolean)
}

function isHttpUrl(value) {
  try {
    const url = new URL(value)
    return (url.protocol === 'http:' || url.protocol === 'https:') && !!url.hostname
  } catch {
    return false
  }
}

// Mirrors AuthenticationSettings.Validate on the server, so the form can show problems before a crawl is started.
// Returns an object keyed by config field name; an empty object means the settings are valid.
export function validateAuthConfig(config) {
  const errors = {}
  const type = config.authType || 'None'
  const blank = v => !v || !String(v).trim()

  if (type === 'Basic' && blank(config.authUsername)) {
    errors.authUsername = 'Username is required for Basic authentication.'
  }

  if (type === 'ApiKey') {
    if (blank(config.authApiKeyHeader)) errors.authApiKeyHeader = 'Header name is required for API key authentication.'
    if (blank(config.authApiKey)) errors.authApiKey = 'API key is required for API key authentication.'
  }

  if (type === 'BearerToken' && blank(config.authBearerToken)) {
    errors.authBearerToken = 'Bearer token is required for bearer token authentication.'
  }

  if (type !== 'None') {
    const invalid = splitLines(config.authCredentialOrigins).filter(o => !isHttpUrl(o))
    if (invalid.length > 0) {
      errors.authCredentialOrigins = 'Not an absolute http or https URL: ' + invalid.join(', ')
    }
  }

  return errors
}

// RedirectOutcome arrives as a string from the server; accept the numeric form too.
const REDIRECT_OUTCOMES = ['None', 'Followed', 'NotFollowed', 'LoopDetected', 'MaxRedirectsExceeded', 'OutOfScope', 'RobotsDisallowed', 'MissingLocation', 'InvalidLocation']

export function getRedirectOutcome(resource) {
  const value = resource ? resource.RedirectOutcome : null
  if (typeof value === 'number') return REDIRECT_OUTCOMES[value] || 'None'
  return value || 'None'
}

// Outcomes that mean the crawler stopped on a problem redirect; None, Followed and NotFollowed are ordinary.
export function isRedirectProblem(outcome) {
  return !['None', 'Followed', 'NotFollowed'].includes(outcome)
}
