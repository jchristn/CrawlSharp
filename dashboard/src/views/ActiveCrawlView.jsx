import React, { useState, useEffect, useRef, useCallback } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { startCrawl, formatBytes, formatDuration, getStatusClass, classifyContentType } from '../utils/api.js'
import { getCrawlById, updateCrawlInHistory, saveCrawlResources } from '../utils/store.js'

export default function ActiveCrawlView({ serverUrl }) {
  const { id } = useParams()
  const navigate = useNavigate()
  const [crawl, setCrawl] = useState(null)
  const [resources, setResources] = useState([])
  const [status, setStatus] = useState('starting')
  const [startTime] = useState(Date.now())
  const [elapsed, setElapsed] = useState(0)
  const [error, setError] = useState(null)
  const abortRef = useRef(null)
  const resourcesRef = useRef([])
  const feedRef = useRef(null)
  const startedRef = useRef(false)

  useEffect(() => {
    const crawlData = getCrawlById(id)
    if (!crawlData) {
      navigate('/history')
      return
    }
    setCrawl(crawlData)

    if (crawlData.status !== 'running') {
      setStatus(crawlData.status)
      return
    }

    if (startedRef.current) return
    startedRef.current = true

    const controller = new AbortController()
    abortRef.current = controller

    startCrawl(
      serverUrl,
      crawlData.settings,
      (resource) => {
        resourcesRef.current = [...resourcesRef.current, resource]
        setResources(prev => [...prev, resource])
      },
      () => {
        setStatus('completed')
        const totalBytes = resourcesRef.current.reduce((s, r) => s + (r.ContentLength || 0), 0)
        const errors = resourcesRef.current.filter(r => r.Status >= 400).length
        updateCrawlInHistory(id, {
          status: 'completed',
          totalPages: resourcesRef.current.length,
          totalBytes,
          errors,
          duration: formatDuration(Date.now() - startTime),
          completedAt: new Date().toISOString()
        })
        saveCrawlResources(id, resourcesRef.current)
      },
      (err) => {
        setError(err.message)
        setStatus('failed')
        updateCrawlInHistory(id, {
          status: 'failed',
          totalPages: resourcesRef.current.length,
          totalBytes: resourcesRef.current.reduce((s, r) => s + (r.ContentLength || 0), 0),
          errors: resourcesRef.current.filter(r => r.Status >= 400).length,
          duration: formatDuration(Date.now() - startTime),
          completedAt: new Date().toISOString(),
          error: err.message
        })
        saveCrawlResources(id, resourcesRef.current)
      },
      controller.signal
    )

    return () => {
      controller.abort()
    }
  }, [id])

  useEffect(() => {
    if (status !== 'starting' && status !== 'running' && status !== 'completed') return
    const timer = setInterval(() => setElapsed(Date.now() - startTime), 1000)
    return () => clearInterval(timer)
  }, [status, startTime])

  useEffect(() => {
    if (resources.length > 0 && status === 'starting') setStatus('running')
  }, [resources.length, status])

  // Auto-scroll feed
  useEffect(() => {
    if (feedRef.current) {
      feedRef.current.scrollTop = feedRef.current.scrollHeight
    }
  }, [resources.length])

  const handleCancel = () => {
    if (abortRef.current) {
      abortRef.current.abort()
      setStatus('cancelled')
      updateCrawlInHistory(id, {
        status: 'cancelled',
        totalPages: resourcesRef.current.length,
        totalBytes: resourcesRef.current.reduce((s, r) => s + (r.ContentLength || 0), 0),
        errors: resourcesRef.current.filter(r => r.Status >= 400).length,
        duration: formatDuration(Date.now() - startTime),
        completedAt: new Date().toISOString()
      })
      saveCrawlResources(id, resourcesRef.current)
    }
  }

  if (!crawl) return null

  const totalBytes = resources.reduce((s, r) => s + (r.ContentLength || 0), 0)
  const errorCount = resources.filter(r => r.Status >= 400).length
  const successCount = resources.filter(r => r.Status >= 200 && r.Status < 400).length

  // Status code distribution
  const statusDist = {}
  resources.forEach(r => {
    const bucket = Math.floor(r.Status / 100) + 'xx'
    statusDist[bucket] = (statusDist[bucket] || 0) + 1
  })

  // Content type distribution
  const typeDist = {}
  resources.forEach(r => {
    const type = classifyContentType(r.ContentType)
    typeDist[type] = (typeDist[type] || 0) + 1
  })

  // Depth distribution
  const depthDist = {}
  resources.forEach(r => {
    const d = r.Depth || 0
    depthDist[d] = (depthDist[d] || 0) + 1
  })

  const maxCount = Math.max(...Object.values(statusDist), 1)
  const maxTypeCount = Math.max(...Object.values(typeDist), 1)

  const isActive = status === 'starting' || status === 'running'

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            {isActive && <span className="status-dot green pulse" />}
            {isActive ? 'Active Crawl' : status === 'completed' ? 'Crawl Complete' : status === 'cancelled' ? 'Crawl Cancelled' : 'Crawl Failed'}
          </h1>
          <p className="page-subtitle mono">{crawl.startUrl}</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          {isActive && (
            <button className="btn btn-danger" onClick={handleCancel}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
              </svg>
              Cancel
            </button>
          )}
          {!isActive && (
            <button className="btn btn-secondary" onClick={() => navigate(`/history/${id}`)}>
              View Results
            </button>
          )}
        </div>
      </div>

      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-card-header">
            <h3>Pages Retrieved</h3>
            <div className="stat-card-icon blue">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                <polyline points="14 2 14 8 20 8" />
              </svg>
            </div>
          </div>
          <div className="value">{resources.length}</div>
          <div className="stat-detail">{successCount} successful</div>
        </div>

        <div className="stat-card">
          <div className="stat-card-header">
            <h3>Data Size</h3>
            <div className="stat-card-icon green">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                <polyline points="7 10 12 15 17 10" />
                <line x1="12" y1="15" x2="12" y2="3" />
              </svg>
            </div>
          </div>
          <div className="value">{formatBytes(totalBytes)}</div>
        </div>

        <div className="stat-card">
          <div className="stat-card-header">
            <h3>Errors</h3>
            <div className={`stat-card-icon ${errorCount > 0 ? 'red' : 'green'}`}>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="10" />
                <line x1="15" y1="9" x2="9" y2="15" />
                <line x1="9" y1="9" x2="15" y2="15" />
              </svg>
            </div>
          </div>
          <div className="value">{errorCount}</div>
          <div className="stat-detail">{resources.length > 0 ? ((errorCount / resources.length) * 100).toFixed(1) : 0}% error rate</div>
        </div>

        <div className="stat-card">
          <div className="stat-card-header">
            <h3>Elapsed</h3>
            <div className="stat-card-icon orange">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="10" />
                <polyline points="12 6 12 12 16 14" />
              </svg>
            </div>
          </div>
          <div className="value">{formatDuration(elapsed)}</div>
          <div className="stat-detail">{resources.length > 0 ? (elapsed / resources.length / 1000).toFixed(1) + 's per page' : '-'}</div>
        </div>
      </div>

      {error && (
        <div className="card" style={{ borderColor: 'var(--danger)' }}>
          <p style={{ color: 'var(--danger)', fontWeight: 600, marginBottom: 4 }}>Error</p>
          <p className="mono text-sm">{error}</p>
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 20, marginBottom: 20 }}>
        <div className="card">
          <div className="card-header">
            <h2>Status Codes</h2>
          </div>
          {Object.keys(statusDist).length === 0 ? (
            <p className="text-muted text-sm">Waiting for responses...</p>
          ) : (
            <div className="distribution-chart">
              {['2xx', '3xx', '4xx', '5xx'].filter(k => statusDist[k]).map(bucket => (
                <div className="distribution-row" key={bucket}>
                  <span className="distribution-label">{bucket}</span>
                  <div className="distribution-bar-bg">
                    <div
                      className={`distribution-bar-fill bar-${bucket}`}
                      style={{ width: `${Math.max((statusDist[bucket] / maxCount) * 100, 8)}%` }}
                    >
                      {statusDist[bucket]}
                    </div>
                  </div>
                  <span className="distribution-count">{statusDist[bucket]}</span>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="card">
          <div className="card-header">
            <h2>Content Types</h2>
          </div>
          {Object.keys(typeDist).length === 0 ? (
            <p className="text-muted text-sm">Waiting for responses...</p>
          ) : (
            <div className="distribution-chart">
              {Object.entries(typeDist).sort((a, b) => b[1] - a[1]).map(([type, count]) => {
                const barClass = type === 'HTML' ? 'bar-html' : type === 'CSS' ? 'bar-css' : type === 'JavaScript' ? 'bar-js' : type === 'Image' ? 'bar-image' : 'bar-other'
                return (
                  <div className="distribution-row" key={type}>
                    <span className="distribution-label">{type}</span>
                    <div className="distribution-bar-bg">
                      <div
                        className={`distribution-bar-fill ${barClass}`}
                        style={{ width: `${Math.max((count / maxTypeCount) * 100, 8)}%` }}
                      >
                        {count}
                      </div>
                    </div>
                    <span className="distribution-count">{count}</span>
                  </div>
                )
              })}
            </div>
          )}
        </div>
      </div>

      <div className="card" style={{ padding: 0 }}>
        <div className="live-feed">
          <div className="live-feed-header">
            <h3>
              {isActive && <span className="status-dot green pulse" />}
              Live Feed
              <span className="text-muted text-sm" style={{ fontWeight: 400 }}>({resources.length} resources)</span>
            </h3>
          </div>
          <div className="live-feed-list" ref={feedRef}>
            {resources.length === 0 && (
              <div style={{ padding: 24, textAlign: 'center', color: 'var(--text-secondary)', fontSize: 13 }}>
                {isActive ? 'Connecting to server and initializing crawl...' : 'No resources retrieved.'}
              </div>
            )}
            {resources.map((r, i) => (
              <div className="live-feed-item" key={i}>
                <span className={`feed-status ${getStatusClass(r.Status)}`}>{r.Status}</span>
                <span className="feed-url" title={r.Url}>{r.Url}</span>
                <span className="feed-type">{classifyContentType(r.ContentType)}</span>
                <span className="feed-size">{formatBytes(r.ContentLength)}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}
