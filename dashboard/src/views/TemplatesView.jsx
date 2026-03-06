import React, { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { getTemplates, deleteTemplate, saveTemplate, generateId } from '../utils/store.js'

export default function TemplatesView() {
  const navigate = useNavigate()
  const [templates, setTemplates] = useState([])
  const [showDelete, setShowDelete] = useState(null)
  const [showDetail, setShowDetail] = useState(null)

  useEffect(() => {
    setTemplates(getTemplates())
  }, [])

  const handleDelete = (id) => {
    deleteTemplate(id)
    setTemplates(getTemplates())
    setShowDelete(null)
  }

  const handleDuplicate = (tpl) => {
    saveTemplate({
      id: generateId(),
      name: tpl.name + ' (copy)',
      config: { ...tpl.config },
      createdAt: new Date().toISOString()
    })
    setTemplates(getTemplates())
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Templates</h1>
          <p className="page-subtitle">Saved crawl configurations for quick re-use</p>
        </div>
        <button className="btn btn-primary" onClick={() => navigate('/crawl/new')}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M5 12h14" />
            <path d="M12 5v14" />
          </svg>
          New Crawl
        </button>
      </div>

      {templates.length === 0 ? (
        <div className="card">
          <div className="empty-state">
            <div className="empty-state-icon">
              <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                <polyline points="14 2 14 8 20 8" />
                <line x1="16" y1="13" x2="8" y2="13" />
                <line x1="16" y1="17" x2="8" y2="17" />
              </svg>
            </div>
            <h3>No templates yet</h3>
            <p>Save crawl settings as a template when starting a new crawl. Templates let you quickly re-use configurations.</p>
            <button className="btn btn-primary" onClick={() => navigate('/crawl/new')}>Create a Crawl</button>
          </div>
        </div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(340px, 1fr))', gap: 16 }}>
          {templates.map(tpl => (
            <div className="card" key={tpl.id} style={{ marginBottom: 0 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 12 }}>
                <div>
                  <h3 style={{ fontSize: 16, fontWeight: 600, marginBottom: 4 }}>{tpl.name}</h3>
                  <p className="text-muted text-sm">Created {new Date(tpl.createdAt).toLocaleDateString()}</p>
                </div>
              </div>

              {tpl.config && (
                <div style={{ marginBottom: 16 }}>
                  {tpl.config.startUrl && (
                    <div style={{ marginBottom: 8 }}>
                      <span className="text-muted text-sm" style={{ display: 'block', marginBottom: 2 }}>Start URL</span>
                      <span className="mono text-sm truncate" style={{ display: 'block' }}>{tpl.config.startUrl}</span>
                    </div>
                  )}
                  <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
                    <div>
                      <span className="text-muted text-sm">Depth: </span>
                      <span className="text-sm">{tpl.config.maxCrawlDepth || 5}</span>
                    </div>
                    <div>
                      <span className="text-muted text-sm">Parallel: </span>
                      <span className="text-sm">{tpl.config.maxParallelTasks || 8}</span>
                    </div>
                    <div>
                      <span className="text-muted text-sm">Headless: </span>
                      <span className="text-sm">{tpl.config.useHeadlessBrowser ? 'Yes' : 'No'}</span>
                    </div>
                  </div>
                </div>
              )}

              <div style={{ display: 'flex', gap: 8 }}>
                <button className="btn btn-sm btn-primary" onClick={() => navigate(`/crawl/new?template=${tpl.id}`)}>
                  Use Template
                </button>
                <button className="btn btn-sm btn-ghost" onClick={() => setShowDetail(tpl)}>
                  View
                </button>
                <button className="btn btn-sm btn-ghost" onClick={() => handleDuplicate(tpl)}>
                  Duplicate
                </button>
                <button
                  className="btn btn-sm btn-ghost"
                  style={{ color: 'var(--danger)', marginLeft: 'auto' }}
                  onClick={() => setShowDelete(tpl.id)}
                >
                  Delete
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {showDelete && (
        <div className="modal-overlay" onClick={() => setShowDelete(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>Delete Template</h2>
            <p>Are you sure you want to delete this template? This action cannot be undone.</p>
            <div className="modal-actions">
              <button className="btn btn-secondary" onClick={() => setShowDelete(null)}>Cancel</button>
              <button className="btn btn-danger" onClick={() => handleDelete(showDelete)}>Delete</button>
            </div>
          </div>
        </div>
      )}

      {showDetail && (
        <div className="modal-overlay" onClick={() => setShowDetail(null)}>
          <div className="modal modal-wide" onClick={e => e.stopPropagation()}>
            <h2>{showDetail.name}</h2>
            <div className="json-viewer">
              {JSON.stringify(showDetail.config, null, 2)}
            </div>
            <div className="modal-actions">
              <button className="btn btn-secondary" onClick={() => setShowDetail(null)}>Close</button>
              <button className="btn btn-primary" onClick={() => { setShowDetail(null); navigate(`/crawl/new?template=${showDetail.id}`) }}>
                Use Template
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
