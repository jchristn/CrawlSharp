import React, { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { checkServerHealth } from '../utils/api.js'
import { getCrawlHistory } from '../utils/store.js'

export default function DashboardView({ serverUrl }) {
  const navigate = useNavigate()
  const [serverOnline, setServerOnline] = useState(null)
  const [history, setHistory] = useState([])

  useEffect(() => {
    checkServerHealth(serverUrl).then(setServerOnline)
    setHistory(getCrawlHistory())
  }, [serverUrl])

  const totalCrawls = history.length
  const completedCrawls = history.filter(c => c.status === 'completed').length
  const totalPages = history.reduce((sum, c) => sum + (c.totalPages || 0), 0)
  const totalBytes = history.reduce((sum, c) => sum + (c.totalBytes || 0), 0)
  const recentCrawls = history.slice(0, 5)

  const activeCrawls = history.filter(c => c.status === 'running')

  const formatBytes = (bytes) => {
    if (!bytes) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i]
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Dashboard</h1>
          <p className="page-subtitle">CrawlSharp server overview and recent activity</p>
        </div>
        <button className="btn btn-primary" onClick={() => navigate('/crawl/new')}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M5 12h14" />
            <path d="M12 5v14" />
          </svg>
          New Crawl
        </button>
      </div>

      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-card-header">
            <h3>Server Status</h3>
            <div className={`stat-card-icon ${serverOnline ? 'green' : serverOnline === false ? 'red' : 'blue'}`}>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M22 12h-4l-3 9L9 3l-3 9H2" />
              </svg>
            </div>
          </div>
          <div className="value" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span className={`status-dot ${serverOnline ? 'green pulse' : serverOnline === false ? 'red' : 'blue'}`} />
            {serverOnline === null ? 'Checking...' : serverOnline ? 'Online' : 'Offline'}
          </div>
          <div className="stat-detail">{serverUrl || window.location.origin}</div>
        </div>

        <div className="stat-card">
          <div className="stat-card-header">
            <h3>Total Crawls</h3>
            <div className="stat-card-icon blue">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="10" />
                <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z" />
                <path d="M2 12h20" />
              </svg>
            </div>
          </div>
          <div className="value">{totalCrawls}</div>
          <div className="stat-detail">{completedCrawls} completed</div>
        </div>

        <div className="stat-card">
          <div className="stat-card-header">
            <h3>Pages Retrieved</h3>
            <div className="stat-card-icon purple">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                <polyline points="14 2 14 8 20 8" />
              </svg>
            </div>
          </div>
          <div className="value">{totalPages.toLocaleString()}</div>
          <div className="stat-detail">Across all crawls</div>
        </div>

        <div className="stat-card">
          <div className="stat-card-header">
            <h3>Data Downloaded</h3>
            <div className="stat-card-icon orange">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                <polyline points="7 10 12 15 17 10" />
                <line x1="12" y1="15" x2="12" y2="3" />
              </svg>
            </div>
          </div>
          <div className="value">{formatBytes(totalBytes)}</div>
          <div className="stat-detail">Total content size</div>
        </div>
      </div>

      {activeCrawls.length > 0 && (
        <div className="card" style={{ borderColor: 'var(--primary)', borderWidth: 2 }}>
          <div className="card-header">
            <h2 style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <span className="status-dot green pulse" />
              Active Crawls
            </h2>
          </div>
          <div className="data-table-wrapper">
            <table>
              <thead>
                <tr>
                  <th>Start URL</th>
                  <th>Pages</th>
                  <th>Started</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {activeCrawls.map(crawl => (
                  <tr key={crawl.id}>
                    <td className="mono truncate" style={{ maxWidth: 400 }}>{crawl.startUrl}</td>
                    <td>{crawl.totalPages || 0}</td>
                    <td className="text-muted text-sm">{new Date(crawl.startedAt).toLocaleString()}</td>
                    <td>
                      <button className="btn btn-sm btn-secondary" onClick={() => navigate(`/crawl/active/${crawl.id}`)}>
                        View
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-header">
          <h2>Recent Crawls</h2>
          {history.length > 5 && (
            <button className="btn btn-sm btn-ghost" onClick={() => navigate('/history')}>View All</button>
          )}
        </div>
        {recentCrawls.length === 0 ? (
          <div className="empty-state">
            <div className="empty-state-icon">
              <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="10" />
                <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z" />
                <path d="M2 12h20" />
              </svg>
            </div>
            <h3>No crawls yet</h3>
            <p>Start your first web crawl to begin exploring and indexing web content.</p>
            <button className="btn btn-primary" onClick={() => navigate('/crawl/new')}>Start Crawling</button>
          </div>
        ) : (
          <div className="data-table-wrapper">
            <table>
              <thead>
                <tr>
                  <th>Status</th>
                  <th>Start URL</th>
                  <th>Pages</th>
                  <th>Size</th>
                  <th>Duration</th>
                  <th>Date</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {recentCrawls.map(crawl => (
                  <tr key={crawl.id}>
                    <td>
                      <span className={`badge ${crawl.status === 'completed' ? 'badge-success' : crawl.status === 'failed' ? 'badge-danger' : crawl.status === 'running' ? 'badge-info' : 'badge-warning'}`}>
                        {crawl.status === 'running' && <span className="status-dot blue pulse" />}
                        {crawl.status}
                      </span>
                    </td>
                    <td className="mono truncate" style={{ maxWidth: 300 }}>{crawl.startUrl}</td>
                    <td>{crawl.totalPages || 0}</td>
                    <td className="text-muted">{formatBytes(crawl.totalBytes)}</td>
                    <td className="text-muted">{crawl.duration || '-'}</td>
                    <td className="text-muted text-sm">{new Date(crawl.startedAt).toLocaleString()}</td>
                    <td>
                      <button
                        className="btn btn-sm btn-ghost"
                        onClick={() => crawl.status === 'running' ? navigate(`/crawl/active/${crawl.id}`) : navigate(`/history/${crawl.id}`)}
                      >
                        View
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
