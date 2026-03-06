const HISTORY_KEY = 'crawlsharp_crawl_history'
const TEMPLATES_KEY = 'crawlsharp_templates'

// Crawl History
export function getCrawlHistory() {
  try {
    return JSON.parse(localStorage.getItem(HISTORY_KEY) || '[]')
  } catch {
    return []
  }
}

export function saveCrawlToHistory(crawl) {
  const history = getCrawlHistory()
  history.unshift(crawl)
  // Keep last 100 crawls (metadata only, not full resources)
  if (history.length > 100) history.length = 100
  localStorage.setItem(HISTORY_KEY, JSON.stringify(history))
}

export function updateCrawlInHistory(id, updates) {
  const history = getCrawlHistory()
  const idx = history.findIndex(c => c.id === id)
  if (idx !== -1) {
    history[idx] = { ...history[idx], ...updates }
    localStorage.setItem(HISTORY_KEY, JSON.stringify(history))
  }
}

export function getCrawlById(id) {
  return getCrawlHistory().find(c => c.id === id) || null
}

export function deleteCrawlFromHistory(id) {
  const history = getCrawlHistory().filter(c => c.id !== id)
  localStorage.setItem(HISTORY_KEY, JSON.stringify(history))
  // Also remove resources
  localStorage.removeItem(`crawlsharp_resources_${id}`)
}

export function clearCrawlHistory() {
  const history = getCrawlHistory()
  history.forEach(c => localStorage.removeItem(`crawlsharp_resources_${c.id}`))
  localStorage.removeItem(HISTORY_KEY)
}

// Resources for a crawl (stored separately to avoid bloating history list)
export function saveCrawlResources(crawlId, resources) {
  try {
    // Strip Data field to save space — keep metadata only
    const lightweight = resources.map(r => ({
      Url: r.Url,
      ParentUrl: r.ParentUrl,
      Filename: r.Filename,
      Depth: r.Depth,
      Status: r.Status,
      ContentLength: r.ContentLength,
      ContentType: r.ContentType,
      ETag: r.ETag,
      MD5Hash: r.MD5Hash,
      SHA1Hash: r.SHA1Hash,
      SHA256Hash: r.SHA256Hash,
      LastModified: r.LastModified,
      Headers: r.Headers
    }))
    localStorage.setItem(`crawlsharp_resources_${crawlId}`, JSON.stringify(lightweight))
  } catch {
    // localStorage might be full — that's ok
  }
}

export function getCrawlResources(crawlId) {
  try {
    return JSON.parse(localStorage.getItem(`crawlsharp_resources_${crawlId}`) || '[]')
  } catch {
    return []
  }
}

// Templates
export function getTemplates() {
  try {
    return JSON.parse(localStorage.getItem(TEMPLATES_KEY) || '[]')
  } catch {
    return []
  }
}

export function saveTemplate(template) {
  const templates = getTemplates()
  templates.push(template)
  localStorage.setItem(TEMPLATES_KEY, JSON.stringify(templates))
}

export function updateTemplate(id, updates) {
  const templates = getTemplates()
  const idx = templates.findIndex(t => t.id === id)
  if (idx !== -1) {
    templates[idx] = { ...templates[idx], ...updates }
    localStorage.setItem(TEMPLATES_KEY, JSON.stringify(templates))
  }
}

export function deleteTemplate(id) {
  const templates = getTemplates().filter(t => t.id !== id)
  localStorage.setItem(TEMPLATES_KEY, JSON.stringify(templates))
}

export function generateId() {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 8)
}
