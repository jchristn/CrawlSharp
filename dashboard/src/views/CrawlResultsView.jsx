import React, { useState, useEffect } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { formatBytes, getStatusClass, classifyContentType } from '../utils/api.js'
import { getCrawlById, getCrawlResources } from '../utils/store.js'

export default function CrawlResultsView({ serverUrl }) {
  const { id } = useParams()
  const navigate = useNavigate()
  const [crawl, setCrawl] = useState(null)
  const [resources, setResources] = useState([])
  const [filter, setFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('all')
  const [typeFilter, setTypeFilter] = useState('all')
  const [sortField, setSortField] = useState('Url')
  const [sortDir, setSortDir] = useState('asc')
  const [selectedResource, setSelectedResource] = useState(null)
  const [activeTab, setActiveTab] = useState('resources')

  useEffect(() => {
    const crawlData = getCrawlById(id)
    if (!crawlData) {
      navigate('/history')
      return
    }
    setCrawl(crawlData)
    setResources(getCrawlResources(id))
  }, [id])

  if (!crawl) return null

  const filtered = resources.filter(r => {
    const matchesText = !filter || r.Url.toLowerCase().includes(filter.toLowerCase())
    const matchesStatus = statusFilter === 'all' || getStatusBucket(r.Status) === statusFilter
    const matchesType = typeFilter === 'all' || classifyContentType(r.ContentType) === typeFilter
    return matchesText && matchesStatus && matchesType
  })

  const sorted = [...filtered].sort((a, b) => {
    let aVal = a[sortField]
    let bVal = b[sortField]
    if (typeof aVal === 'string') aVal = aVal.toLowerCase()
    if (typeof bVal === 'string') bVal = bVal.toLowerCase()
    if (aVal < bVal) return sortDir === 'asc' ? -1 : 1
    if (aVal > bVal) return sortDir === 'asc' ? 1 : -1
    return 0
  })

  const handleSort = (field) => {
    if (sortField === field) {
      setSortDir(d => d === 'asc' ? 'desc' : 'asc')
    } else {
      setSortField(field)
      setSortDir('asc')
    }
  }

  // Compute stats
  const statusDist = {}
  const typeDist = {}
  const depthDist = {}
  const brokenLinks = []

  resources.forEach(r => {
    const bucket = getStatusBucket(r.Status)
    statusDist[bucket] = (statusDist[bucket] || 0) + 1
    const type = classifyContentType(r.ContentType)
    typeDist[type] = (typeDist[type] || 0) + 1
    const d = r.Depth || 0
    depthDist[d] = (depthDist[d] || 0) + 1
    if (r.Status >= 400) brokenLinks.push(r)
  })

  const contentTypes = [...new Set(resources.map(r => classifyContentType(r.ContentType)))].sort()
  const totalBytes = resources.reduce((s, r) => s + (r.ContentLength || 0), 0)

  const SortIcon = ({ field }) => {
    if (sortField !== field) return <span style={{ opacity: 0.3 }}> &#8597;</span>
    return <span> {sortDir === 'asc' ? '&#8593;' : '&#8595;'}</span>
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Crawl Results</h1>
          <p className="page-subtitle mono">{crawl.startUrl}</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-secondary" onClick={() => navigate('/history')}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <line x1="19" y1="12" x2="5" y2="12" />
              <polyline points="12 19 5 12 12 5" />
            </svg>
            Back
          </button>
          <button className="btn btn-primary" onClick={() => {
            navigate(`/crawl/new?template=`)
            // Re-run: navigate to new crawl with same config
            if (crawl.config) {
              navigate('/crawl/new')
            }
          }}>
            Re-run
          </button>
        </div>
      </div>

      <div className="stats-grid">
        <div className="stat-card">
          <h3>Total Pages</h3>
          <div className="value">{resources.length}</div>
        </div>
        <div className="stat-card">
          <h3>Total Size</h3>
          <div className="value">{formatBytes(totalBytes)}</div>
        </div>
        <div className="stat-card">
          <h3>Errors</h3>
          <div className="value" style={{ color: brokenLinks.length > 0 ? 'var(--danger)' : 'var(--success)' }}>
            {brokenLinks.length}
          </div>
        </div>
        <div className="stat-card">
          <h3>Duration</h3>
          <div className="value">{crawl.duration || '-'}</div>
        </div>
      </div>

      <div className="tabs">
        <button className={`tab ${activeTab === 'resources' ? 'active' : ''}`} onClick={() => setActiveTab('resources')}>
          Resources ({resources.length})
        </button>
        <button className={`tab ${activeTab === 'broken' ? 'active' : ''}`} onClick={() => setActiveTab('broken')}>
          Broken Links ({brokenLinks.length})
        </button>
        <button className={`tab ${activeTab === 'stats' ? 'active' : ''}`} onClick={() => setActiveTab('stats')}>
          Statistics
        </button>
        <button className={`tab ${activeTab === 'settings' ? 'active' : ''}`} onClick={() => setActiveTab('settings')}>
          Settings
        </button>
      </div>

      {activeTab === 'resources' && (
        <div className="card">
          <div style={{ display: 'flex', gap: 12, marginBottom: 16 }}>
            <div className="form-group" style={{ flex: 1, marginBottom: 0 }}>
              <input
                type="text"
                placeholder="Filter by URL..."
                value={filter}
                onChange={e => setFilter(e.target.value)}
              />
            </div>
            <div className="form-group" style={{ width: 140, marginBottom: 0 }}>
              <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}>
                <option value="all">All Codes</option>
                <option value="2xx">2xx Success</option>
                <option value="3xx">3xx Redirect</option>
                <option value="4xx">4xx Client Error</option>
                <option value="5xx">5xx Server Error</option>
              </select>
            </div>
            <div className="form-group" style={{ width: 140, marginBottom: 0 }}>
              <select value={typeFilter} onChange={e => setTypeFilter(e.target.value)}>
                <option value="all">All Types</option>
                {contentTypes.map(t => <option key={t} value={t}>{t}</option>)}
              </select>
            </div>
          </div>

          <div className="data-table-wrapper" style={{ maxHeight: 600, overflowY: 'auto' }}>
            <table>
              <thead>
                <tr>
                  <th style={{ cursor: 'pointer' }} onClick={() => handleSort('Status')}>
                    Status <SortIcon field="Status" />
                  </th>
                  <th style={{ cursor: 'pointer' }} onClick={() => handleSort('Url')}>
                    URL <SortIcon field="Url" />
                  </th>
                  <th style={{ cursor: 'pointer' }} onClick={() => handleSort('ContentType')}>
                    Type <SortIcon field="ContentType" />
                  </th>
                  <th style={{ cursor: 'pointer' }} onClick={() => handleSort('ContentLength')}>
                    Size <SortIcon field="ContentLength" />
                  </th>
                  <th style={{ cursor: 'pointer' }} onClick={() => handleSort('Depth')}>
                    Depth <SortIcon field="Depth" />
                  </th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {sorted.slice(0, 500).map((r, i) => (
                  <tr key={i}>
                    <td>
                      <span className={`badge ${r.Status >= 200 && r.Status < 300 ? 'badge-success' : r.Status >= 300 && r.Status < 400 ? 'badge-info' : r.Status >= 400 && r.Status < 500 ? 'badge-warning' : 'badge-danger'}`}>
                        {r.Status}
                      </span>
                    </td>
                    <td className="mono truncate" style={{ maxWidth: 400 }} title={r.Url}>{r.Url}</td>
                    <td className="text-muted text-sm">{classifyContentType(r.ContentType)}</td>
                    <td className="text-muted">{formatBytes(r.ContentLength)}</td>
                    <td className="text-muted">{r.Depth}</td>
                    <td>
                      <button className="btn btn-sm btn-ghost" onClick={() => setSelectedResource(r)}>
                        Details
                      </button>
                    </td>
                  </tr>
                ))}
                {sorted.length > 500 && (
                  <tr>
                    <td colSpan="6" style={{ textAlign: 'center', color: 'var(--text-secondary)', padding: 16 }}>
                      Showing first 500 of {sorted.length} resources. Use filters to narrow results.
                    </td>
                  </tr>
                )}
                {sorted.length === 0 && (
                  <tr>
                    <td colSpan="6" style={{ textAlign: 'center', color: 'var(--text-secondary)', padding: 32 }}>
                      No resources match your filters.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {activeTab === 'broken' && (
        <div className="card">
          {brokenLinks.length === 0 ? (
            <div className="empty-state" style={{ padding: 40 }}>
              <h3 style={{ color: 'var(--success)' }}>No broken links found</h3>
              <p>All resources returned successful status codes.</p>
            </div>
          ) : (
            <div className="data-table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>Status</th>
                    <th>URL</th>
                    <th>Parent URL</th>
                    <th>Depth</th>
                  </tr>
                </thead>
                <tbody>
                  {brokenLinks.map((r, i) => (
                    <tr key={i}>
                      <td>
                        <span className={`badge ${r.Status >= 400 && r.Status < 500 ? 'badge-warning' : 'badge-danger'}`}>{r.Status}</span>
                      </td>
                      <td className="mono truncate" style={{ maxWidth: 350 }} title={r.Url}>{r.Url}</td>
                      <td className="mono truncate text-muted" style={{ maxWidth: 250 }} title={r.ParentUrl}>{r.ParentUrl || '-'}</td>
                      <td className="text-muted">{r.Depth}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {activeTab === 'stats' && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 20 }}>
          <div className="card">
            <div className="card-header"><h2>Status Code Distribution</h2></div>
            <div className="distribution-chart">
              {['2xx', '3xx', '4xx', '5xx'].filter(k => statusDist[k]).map(bucket => {
                const maxVal = Math.max(...Object.values(statusDist))
                return (
                  <div className="distribution-row" key={bucket}>
                    <span className="distribution-label">{bucket}</span>
                    <div className="distribution-bar-bg">
                      <div className={`distribution-bar-fill bar-${bucket}`} style={{ width: `${Math.max((statusDist[bucket] / maxVal) * 100, 8)}%` }}>
                        {statusDist[bucket]}
                      </div>
                    </div>
                    <span className="distribution-count">{statusDist[bucket]}</span>
                  </div>
                )
              })}
            </div>
          </div>

          <div className="card">
            <div className="card-header"><h2>Content Type Distribution</h2></div>
            <div className="distribution-chart">
              {Object.entries(typeDist).sort((a, b) => b[1] - a[1]).map(([type, count]) => {
                const maxVal = Math.max(...Object.values(typeDist))
                const barClass = type === 'HTML' ? 'bar-html' : type === 'CSS' ? 'bar-css' : type === 'JavaScript' ? 'bar-js' : type === 'Image' ? 'bar-image' : 'bar-other'
                return (
                  <div className="distribution-row" key={type}>
                    <span className="distribution-label">{type}</span>
                    <div className="distribution-bar-bg">
                      <div className={`distribution-bar-fill ${barClass}`} style={{ width: `${Math.max((count / maxVal) * 100, 8)}%` }}>
                        {count}
                      </div>
                    </div>
                    <span className="distribution-count">{count}</span>
                  </div>
                )
              })}
            </div>
          </div>

          <div className="card" style={{ gridColumn: '1 / -1' }}>
            <div className="card-header"><h2>Depth Distribution</h2></div>
            <div className="distribution-chart">
              {Object.entries(depthDist).sort((a, b) => Number(a[0]) - Number(b[0])).map(([depth, count]) => {
                const maxVal = Math.max(...Object.values(depthDist))
                return (
                  <div className="distribution-row" key={depth}>
                    <span className="distribution-label">Depth {depth}</span>
                    <div className="distribution-bar-bg">
                      <div className="distribution-bar-fill bar-html" style={{ width: `${Math.max((count / maxVal) * 100, 8)}%` }}>
                        {count}
                      </div>
                    </div>
                    <span className="distribution-count">{count}</span>
                  </div>
                )
              })}
            </div>
          </div>
        </div>
      )}

      {activeTab === 'settings' && (
        <div className="card">
          <div className="card-header"><h2>Crawl Settings</h2></div>
          <div className="json-viewer">
            {JSON.stringify(crawl.settings || {}, null, 2)}
          </div>
        </div>
      )}

      {selectedResource && (
        <div className="modal-overlay" onClick={() => setSelectedResource(null)}>
          <div className="modal modal-wide" onClick={e => e.stopPropagation()}>
            <h2>Resource Details</h2>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginBottom: 16 }}>
              <div>
                <div className="form-group">
                  <label>URL</label>
                  <div className="mono text-sm" style={{ wordBreak: 'break-all' }}>{selectedResource.Url}</div>
                </div>
                <div className="form-group">
                  <label>Parent URL</label>
                  <div className="mono text-sm" style={{ wordBreak: 'break-all' }}>{selectedResource.ParentUrl || '-'}</div>
                </div>
                <div className="form-group">
                  <label>Content Type</label>
                  <div>{selectedResource.ContentType || '-'}</div>
                </div>
              </div>
              <div>
                <div className="form-group">
                  <label>Status</label>
                  <span className={`badge ${selectedResource.Status >= 200 && selectedResource.Status < 300 ? 'badge-success' : selectedResource.Status >= 300 && selectedResource.Status < 400 ? 'badge-info' : 'badge-warning'}`}>
                    {selectedResource.Status}
                  </span>
                </div>
                <div className="form-group">
                  <label>Content Length</label>
                  <div>{formatBytes(selectedResource.ContentLength)}</div>
                </div>
                <div className="form-group">
                  <label>Depth</label>
                  <div>{selectedResource.Depth}</div>
                </div>
                <div className="form-group">
                  <label>Last Modified</label>
                  <div>{selectedResource.LastModified || '-'}</div>
                </div>
                <div className="form-group">
                  <label>ETag</label>
                  <div className="mono text-sm">{selectedResource.ETag || '-'}</div>
                </div>
              </div>
            </div>
            {(selectedResource.MD5Hash || selectedResource.SHA1Hash || selectedResource.SHA256Hash) && (
              <div style={{ marginBottom: 16 }}>
                <label style={{ fontWeight: 600, fontSize: 13, marginBottom: 8, display: 'block' }}>Hashes</label>
                <div style={{ background: 'var(--code-bg)', padding: 12, borderRadius: 8, fontSize: 12, fontFamily: 'monospace' }}>
                  {selectedResource.MD5Hash && <div><strong>MD5:</strong> {selectedResource.MD5Hash}</div>}
                  {selectedResource.SHA1Hash && <div><strong>SHA1:</strong> {selectedResource.SHA1Hash}</div>}
                  {selectedResource.SHA256Hash && <div><strong>SHA256:</strong> {selectedResource.SHA256Hash}</div>}
                </div>
              </div>
            )}
            {selectedResource.Headers && Object.keys(selectedResource.Headers).length > 0 && (
              <div>
                <label style={{ fontWeight: 600, fontSize: 13, marginBottom: 8, display: 'block' }}>Response Headers</label>
                <div className="json-viewer" style={{ maxHeight: 200 }}>
                  {JSON.stringify(selectedResource.Headers, null, 2)}
                </div>
              </div>
            )}
            <div className="modal-actions">
              <button className="btn btn-secondary" onClick={() => setSelectedResource(null)}>Close</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function getStatusBucket(status) {
  if (status >= 200 && status < 300) return '2xx'
  if (status >= 300 && status < 400) return '3xx'
  if (status >= 400 && status < 500) return '4xx'
  return '5xx'
}
