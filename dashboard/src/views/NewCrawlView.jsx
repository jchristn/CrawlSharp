import React, { useState, useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { buildSettingsPayload } from '../utils/api.js'
import { getTemplates, generateId, saveCrawlToHistory, saveTemplate } from '../utils/store.js'

const defaultConfig = {
  startUrl: '',
  userAgent: 'CrawlSharp',
  maxCrawlDepth: '5',
  maxParallelTasks: '8',
  pageTimeoutMs: '30000',
  throttleMs: '5000',
  retryOn429: true,
  maxRetries: '3',
  retryMinBackoffMs: '1000',
  retryMaxBackoffMs: '30000',
  retryBackoffJitter: true,
  requestDelayMs: '2500',
  useHeadlessBrowser: false,
  autoExpandCollapsibles: false,
  postLoadDelayMs: '0',
  postInteractionDelayMs: '250',
  maxExpansionPasses: '2',
  expansionSelectors: '',
  ignoreRobotsText: false,
  includeSitemap: true,
  followLinks: true,
  followRedirects: true,
  followExternalLinks: true,
  restrictToChildUrls: true,
  restrictToSameSubdomain: true,
  restrictToSameRootDomain: true,
  allowedDomains: '',
  deniedDomains: '',
  authType: 'None',
  authUsername: '',
  authPassword: '',
  authApiKeyHeader: '',
  authApiKey: '',
  authBearerToken: ''
}

export default function NewCrawlView({ serverUrl }) {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [config, setConfig] = useState({ ...defaultConfig })
  const [templates, setTemplates] = useState([])
  const [saveAsTemplate, setSaveAsTemplate] = useState(false)
  const [templateName, setTemplateName] = useState('')
  const [showJson, setShowJson] = useState(false)

  useEffect(() => {
    setTemplates(getTemplates())
    const tplId = searchParams.get('template')
    if (tplId) {
      const tpl = getTemplates().find(t => t.id === tplId)
      if (tpl) setConfig({ ...defaultConfig, ...tpl.config })
    }
  }, [searchParams])

  const updateConfig = (key, value) => {
    setConfig(prev => ({ ...prev, [key]: value }))
  }

  const handleStartCrawl = () => {
    if (!config.startUrl.trim()) return

    const crawlId = generateId()

    if (saveAsTemplate && templateName.trim()) {
      saveTemplate({
        id: generateId(),
        name: templateName.trim(),
        config: { ...config },
        createdAt: new Date().toISOString()
      })
    }

    saveCrawlToHistory({
      id: crawlId,
      startUrl: config.startUrl.trim(),
      status: 'running',
      totalPages: 0,
      totalBytes: 0,
      errors: 0,
      startedAt: new Date().toISOString(),
      settings: buildSettingsPayload(config),
      config: { ...config }
    })

    navigate(`/crawl/active/${crawlId}`)
  }

  const payload = buildSettingsPayload(config)

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>New Crawl</h1>
          <p className="page-subtitle">Configure and launch a web crawl</p>
        </div>
      </div>

      {templates.length > 0 && (
        <div className="card">
          <div className="card-header">
            <h2>Load from Template</h2>
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {templates.map(tpl => (
              <button
                key={tpl.id}
                className="btn btn-sm btn-secondary"
                onClick={() => setConfig({ ...defaultConfig, ...tpl.config })}
              >
                {tpl.name}
              </button>
            ))}
          </div>
        </div>
      )}

      <div className="card">
        <div className="form-group">
          <label>Start URL *</label>
          <input
            type="url"
            value={config.startUrl}
            onChange={e => updateConfig('startUrl', e.target.value)}
            placeholder="https://example.com"
            autoFocus
          />
          <p className="form-hint">The URL to begin crawling from.</p>
        </div>

        <div className="form-section">
          <div className="form-section-title">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="3" />
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z" />
            </svg>
            General Settings
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>User Agent</label>
              <input
                type="text"
                value={config.userAgent}
                onChange={e => updateConfig('userAgent', e.target.value)}
              />
            </div>
            <div className="form-group">
              <label>Max Crawl Depth</label>
              <input
                type="number"
                min="0"
                value={config.maxCrawlDepth}
                onChange={e => updateConfig('maxCrawlDepth', e.target.value)}
              />
            </div>
          </div>
          <div className="form-row-3">
            <div className="form-group">
              <label>Parallel Tasks</label>
              <input
                type="number"
                min="1"
                value={config.maxParallelTasks}
                onChange={e => updateConfig('maxParallelTasks', e.target.value)}
              />
            </div>
            <div className="form-group">
              <label>Page Timeout (ms)</label>
              <input
                type="number"
                min="1000"
                value={config.pageTimeoutMs}
                onChange={e => updateConfig('pageTimeoutMs', e.target.value)}
              />
            </div>
            <div className="form-group">
              <label>Throttle (ms)</label>
              <input
                type="number"
                min="0"
                value={config.throttleMs}
                onChange={e => updateConfig('throttleMs', e.target.value)}
              />
              <p className="form-hint">Delay on 429 responses</p>
            </div>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>Request Delay (ms)</label>
              <input
                type="number"
                min="0"
                value={config.requestDelayMs}
                onChange={e => updateConfig('requestDelayMs', e.target.value)}
              />
              <p className="form-hint">Delay between each request</p>
            </div>
            <div></div>
          </div>
        </div>

        <div className="form-section">
          <div className="form-section-title">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <polyline points="1 4 1 10 7 10" />
              <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
            </svg>
            Retry on 429
          </div>
          <div className="toggle-group">
            <div>
              <label>Enable Retry on 429</label>
              <div className="toggle-hint">Automatically retry requests that receive a 429 (Too Many Requests) response</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.retryOn429} onChange={e => updateConfig('retryOn429', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('retryOn429', !config.retryOn429)} />
            </div>
          </div>
          {config.retryOn429 && (
            <>
              <div className="form-row-3">
                <div className="form-group">
                  <label>Max Retries</label>
                  <input
                    type="number"
                    min="1"
                    value={config.maxRetries}
                    onChange={e => updateConfig('maxRetries', e.target.value)}
                  />
                </div>
                <div className="form-group">
                  <label>Min Backoff (ms)</label>
                  <input
                    type="number"
                    min="100"
                    value={config.retryMinBackoffMs}
                    onChange={e => updateConfig('retryMinBackoffMs', e.target.value)}
                  />
                </div>
                <div className="form-group">
                  <label>Max Backoff (ms)</label>
                  <input
                    type="number"
                    min="1000"
                    value={config.retryMaxBackoffMs}
                    onChange={e => updateConfig('retryMaxBackoffMs', e.target.value)}
                  />
                </div>
              </div>
              <div className="toggle-group">
                <div>
                  <label>Backoff Jitter</label>
                  <div className="toggle-hint">Add random jitter to backoff delay to avoid thundering herd</div>
                </div>
                <div className="toggle-switch">
                  <input type="checkbox" checked={config.retryBackoffJitter} onChange={e => updateConfig('retryBackoffJitter', e.target.checked)} />
                  <span className="toggle-slider" onClick={() => updateConfig('retryBackoffJitter', !config.retryBackoffJitter)} />
                </div>
              </div>
            </>
          )}
        </div>

        <div className="form-section">
          <div className="form-section-title">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" />
              <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" />
            </svg>
            Crawl Behavior
          </div>
          <div className="toggle-group">
            <div>
              <label>Follow Links</label>
              <div className="toggle-hint">Discover and follow links found in pages</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.followLinks} onChange={e => updateConfig('followLinks', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('followLinks', !config.followLinks)} />
            </div>
          </div>
          <div className="toggle-group">
            <div>
              <label>Follow Redirects</label>
              <div className="toggle-hint">Automatically follow HTTP redirects</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.followRedirects} onChange={e => updateConfig('followRedirects', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('followRedirects', !config.followRedirects)} />
            </div>
          </div>
          <div className="toggle-group">
            <div>
              <label>Follow External Links</label>
              <div className="toggle-hint">Follow links to external domains</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.followExternalLinks} onChange={e => updateConfig('followExternalLinks', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('followExternalLinks', !config.followExternalLinks)} />
            </div>
          </div>
          <div className="toggle-group">
            <div>
              <label>Restrict to Child URLs</label>
              <div className="toggle-hint">Only follow links that are children of the start URL</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.restrictToChildUrls} onChange={e => updateConfig('restrictToChildUrls', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('restrictToChildUrls', !config.restrictToChildUrls)} />
            </div>
          </div>
          <div className="toggle-group">
            <div>
              <label>Restrict to Same Subdomain</label>
              <div className="toggle-hint">Only follow links within the same subdomain</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.restrictToSameSubdomain} onChange={e => updateConfig('restrictToSameSubdomain', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('restrictToSameSubdomain', !config.restrictToSameSubdomain)} />
            </div>
          </div>
          <div className="toggle-group">
            <div>
              <label>Restrict to Same Root Domain</label>
              <div className="toggle-hint">Only follow links within the same root domain</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.restrictToSameRootDomain} onChange={e => updateConfig('restrictToSameRootDomain', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('restrictToSameRootDomain', !config.restrictToSameRootDomain)} />
            </div>
          </div>
          <div className="toggle-group">
            <div>
              <label>Use Headless Browser</label>
              <div className="toggle-hint">Use Playwright Firefox for JavaScript-heavy sites</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.useHeadlessBrowser} onChange={e => updateConfig('useHeadlessBrowser', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('useHeadlessBrowser', !config.useHeadlessBrowser)} />
            </div>
          </div>
          {config.useHeadlessBrowser && (
            <>
              <div className="toggle-group">
                <div>
                  <label>Auto-Expand Collapsibles</label>
                  <div className="toggle-hint">Opt in to opening &lt;details&gt; elements and clicking common collapsible controls before HTML capture</div>
                </div>
                <div className="toggle-switch">
                  <input type="checkbox" checked={config.autoExpandCollapsibles} onChange={e => updateConfig('autoExpandCollapsibles', e.target.checked)} />
                  <span className="toggle-slider" onClick={() => updateConfig('autoExpandCollapsibles', !config.autoExpandCollapsibles)} />
                </div>
              </div>
              {config.autoExpandCollapsibles ? (
                <>
                  <div className="form-row-3">
                    <div className="form-group">
                      <label>Post-Load Delay (ms)</label>
                      <input
                        type="number"
                        min="0"
                        value={config.postLoadDelayMs}
                        onChange={e => updateConfig('postLoadDelayMs', e.target.value)}
                      />
                      <p className="form-hint">Wait after navigation before expansion starts</p>
                    </div>
                    <div className="form-group">
                      <label>Post-Interaction Delay (ms)</label>
                      <input
                        type="number"
                        min="0"
                        value={config.postInteractionDelayMs}
                        onChange={e => updateConfig('postInteractionDelayMs', e.target.value)}
                      />
                      <p className="form-hint">Allow the DOM to settle after each expansion pass</p>
                    </div>
                    <div className="form-group">
                      <label>Max Expansion Passes</label>
                      <input
                        type="number"
                        min="1"
                        value={config.maxExpansionPasses}
                        onChange={e => updateConfig('maxExpansionPasses', e.target.value)}
                      />
                      <p className="form-hint">Repeat expansion for nested lazy content</p>
                    </div>
                  </div>
                  <div className="form-group">
                    <label>Expansion Selectors</label>
                    <textarea
                      rows="3"
                      value={config.expansionSelectors}
                      onChange={e => updateConfig('expansionSelectors', e.target.value)}
                      placeholder={".faq-toggle\n[data-expand='more']"}
                    />
                    <p className="form-hint">Optional CSS selectors, one per line, to click in addition to the built-in collapsible patterns</p>
                  </div>
                </>
              ) : (
                <p className="form-hint">Headless expansion settings stay idle until auto-expand is enabled.</p>
              )}
            </>
          )}
          <div className="toggle-group">
            <div>
              <label>Ignore robots.txt</label>
              <div className="toggle-hint">Skip robots.txt restrictions (use responsibly)</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.ignoreRobotsText} onChange={e => updateConfig('ignoreRobotsText', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('ignoreRobotsText', !config.ignoreRobotsText)} />
            </div>
          </div>
          <div className="toggle-group">
            <div>
              <label>Include Sitemap</label>
              <div className="toggle-hint">Process sitemap.xml for URL discovery</div>
            </div>
            <div className="toggle-switch">
              <input type="checkbox" checked={config.includeSitemap} onChange={e => updateConfig('includeSitemap', e.target.checked)} />
              <span className="toggle-slider" onClick={() => updateConfig('includeSitemap', !config.includeSitemap)} />
            </div>
          </div>
        </div>

        <div className="form-section">
          <div className="form-section-title">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="10" />
              <line x1="4.93" y1="4.93" x2="19.07" y2="19.07" />
            </svg>
            Domain Restrictions
          </div>
          <div className="form-row">
            <div className="form-group">
              <label>Allowed Domains</label>
              <textarea
                rows="3"
                value={config.allowedDomains}
                onChange={e => updateConfig('allowedDomains', e.target.value)}
                placeholder="One domain per line"
              />
              <p className="form-hint">If set, only these domains will be crawled</p>
            </div>
            <div className="form-group">
              <label>Denied Domains</label>
              <textarea
                rows="3"
                value={config.deniedDomains}
                onChange={e => updateConfig('deniedDomains', e.target.value)}
                placeholder="One domain per line"
              />
              <p className="form-hint">These domains will be excluded from crawling</p>
            </div>
          </div>
        </div>

        <div className="form-section">
          <div className="form-section-title">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
              <path d="M7 11V7a5 5 0 0 1 10 0v4" />
            </svg>
            Authentication
          </div>
          <div className="form-group">
            <label>Authentication Type</label>
            <select value={config.authType} onChange={e => updateConfig('authType', e.target.value)}>
              <option value="None">None</option>
              <option value="Basic">Basic (Username / Password)</option>
              <option value="ApiKey">API Key (Custom Header)</option>
              <option value="BearerToken">Bearer Token</option>
            </select>
          </div>

          {config.authType === 'Basic' && (
            <div className="form-row">
              <div className="form-group">
                <label>Username</label>
                <input type="text" value={config.authUsername} onChange={e => updateConfig('authUsername', e.target.value)} />
              </div>
              <div className="form-group">
                <label>Password</label>
                <input type="password" value={config.authPassword} onChange={e => updateConfig('authPassword', e.target.value)} />
              </div>
            </div>
          )}

          {config.authType === 'ApiKey' && (
            <div className="form-row">
              <div className="form-group">
                <label>Header Name</label>
                <input type="text" value={config.authApiKeyHeader} onChange={e => updateConfig('authApiKeyHeader', e.target.value)} placeholder="X-Api-Key" />
              </div>
              <div className="form-group">
                <label>API Key</label>
                <input type="password" value={config.authApiKey} onChange={e => updateConfig('authApiKey', e.target.value)} />
              </div>
            </div>
          )}

          {config.authType === 'BearerToken' && (
            <div className="form-group">
              <label>Bearer Token</label>
              <input type="password" value={config.authBearerToken} onChange={e => updateConfig('authBearerToken', e.target.value)} />
            </div>
          )}
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 20, padding: '12px 16px', background: 'var(--bg)', borderRadius: 8, border: '1px solid var(--border)' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', marginBottom: 0, fontSize: 14 }}>
            <input type="checkbox" checked={saveAsTemplate} onChange={e => setSaveAsTemplate(e.target.checked)} style={{ width: 'auto' }} />
            Save as template
          </label>
          {saveAsTemplate && (
            <input
              type="text"
              value={templateName}
              onChange={e => setTemplateName(e.target.value)}
              placeholder="Template name"
              style={{ flex: 1, padding: '6px 12px', border: '1px solid var(--border)', borderRadius: 6, background: 'var(--card-bg)', color: 'var(--text)', fontSize: 14 }}
            />
          )}
        </div>

        <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
          <button
            className="btn btn-primary"
            onClick={handleStartCrawl}
            disabled={!config.startUrl.trim()}
            style={{ opacity: config.startUrl.trim() ? 1 : 0.5 }}
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
              <polygon points="5 3 19 12 5 21 5 3" />
            </svg>
            Start Crawl
          </button>
          <button className="btn btn-ghost" onClick={() => setShowJson(!showJson)}>
            {showJson ? 'Hide' : 'Show'} JSON Payload
          </button>
        </div>

        {showJson && (
          <div className="json-viewer" style={{ marginTop: 16 }}>
            {JSON.stringify(payload, null, 2)}
          </div>
        )}
      </div>
    </div>
  )
}
