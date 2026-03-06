import React, { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { formatBytes } from '../utils/api.js'
import { getCrawlHistory, deleteCrawlFromHistory, clearCrawlHistory } from '../utils/store.js'

export default function CrawlHistoryView() {
  const navigate = useNavigate()
  const [history, setHistory] = useState([])
  const [filter, setFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('all')
  const [showClearConfirm, setShowClearConfirm] = useState(false)

  useEffect(() => {
    setHistory(getCrawlHistory())
  }, [])

  const filtered = history.filter(c => {
    const matchesText = !filter || c.startUrl.toLowerCase().includes(filter.toLowerCase())
    const matchesStatus = statusFilter === 'all' || c.status === statusFilter
    return matchesText && matchesStatus
  })

  const handleDelete = (id) => {
    deleteCrawlFromHistory(id)
    setHistory(getCrawlHistory())
  }

  const handleClearAll = () => {
    clearCrawlHistory()
    setHistory([])
    setShowClearConfirm(false)
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Crawl History</h1>
          <p className="page-subtitle">{history.length} crawl{history.length !== 1 ? 's' : ''} recorded</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-primary" onClick={() => navigate('/crawl/new')}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
              <path d="M5 12h14" />
              <path d="M12 5v14" />
            </svg>
            New Crawl
          </button>
          {history.length > 0 && (
            <button className="btn btn-ghost" onClick={() => setShowClearConfirm(true)}>Clear All</button>
          )}
        </div>
      </div>

      {history.length > 0 && (
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
            <div className="form-group" style={{ width: 160, marginBottom: 0 }}>
              <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}>
                <option value="all">All Statuses</option>
                <option value="completed">Completed</option>
                <option value="failed">Failed</option>
                <option value="cancelled">Cancelled</option>
                <option value="running">Running</option>
              </select>
            </div>
          </div>

          <div className="data-table-wrapper">
            <table>
              <thead>
                <tr>
                  <th>Status</th>
                  <th>Start URL</th>
                  <th>Pages</th>
                  <th>Errors</th>
                  <th>Size</th>
                  <th>Duration</th>
                  <th>Date</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {filtered.map(crawl => (
                  <tr key={crawl.id}>
                    <td>
                      <span className={`badge ${crawl.status === 'completed' ? 'badge-success' : crawl.status === 'failed' ? 'badge-danger' : crawl.status === 'running' ? 'badge-info' : 'badge-warning'}`}>
                        {crawl.status === 'running' && <span className="status-dot blue pulse" />}
                        {crawl.status}
                      </span>
                    </td>
                    <td className="mono truncate" style={{ maxWidth: 350 }}>{crawl.startUrl}</td>
                    <td>{crawl.totalPages || 0}</td>
                    <td>
                      {(crawl.errors || 0) > 0
                        ? <span style={{ color: 'var(--danger)', fontWeight: 600 }}>{crawl.errors}</span>
                        : <span className="text-muted">0</span>
                      }
                    </td>
                    <td className="text-muted">{formatBytes(crawl.totalBytes)}</td>
                    <td className="text-muted">{crawl.duration || '-'}</td>
                    <td className="text-muted text-sm">{new Date(crawl.startedAt).toLocaleString()}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 4 }}>
                        <button
                          className="btn btn-sm btn-ghost"
                          onClick={() => crawl.status === 'running' ? navigate(`/crawl/active/${crawl.id}`) : navigate(`/history/${crawl.id}`)}
                        >
                          View
                        </button>
                        <button
                          className="btn btn-sm btn-ghost"
                          style={{ color: 'var(--danger)' }}
                          onClick={() => handleDelete(crawl.id)}
                          title="Delete"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <polyline points="3 6 5 6 21 6" />
                            <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                          </svg>
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan="8" style={{ textAlign: 'center', padding: 32, color: 'var(--text-secondary)' }}>
                      No crawls match your filters.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {history.length === 0 && (
        <div className="card">
          <div className="empty-state">
            <div className="empty-state-icon">
              <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="10" />
                <polyline points="12 6 12 12 16 14" />
              </svg>
            </div>
            <h3>No crawl history</h3>
            <p>Your completed crawls will appear here. Start a new crawl to get going.</p>
            <button className="btn btn-primary" onClick={() => navigate('/crawl/new')}>Start Crawling</button>
          </div>
        </div>
      )}

      {showClearConfirm && (
        <div className="modal-overlay" onClick={() => setShowClearConfirm(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>Clear All History</h2>
            <p>This will permanently delete all crawl history and saved results. This action cannot be undone.</p>
            <div className="modal-actions">
              <button className="btn btn-secondary" onClick={() => setShowClearConfirm(false)}>Cancel</button>
              <button className="btn btn-danger" onClick={handleClearAll}>Clear All</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
