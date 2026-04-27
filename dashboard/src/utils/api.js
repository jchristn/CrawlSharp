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
  }

  return settings
}
