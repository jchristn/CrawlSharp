import React, { useEffect, useMemo, useState } from 'react'

function isValidServerUrl(url) {
  const trimmed = url.trim()
  if (!trimmed) return true

  try {
    const parsed = new URL(trimmed)
    return parsed.protocol === 'http:' || parsed.protocol === 'https:'
  } catch {
    return false
  }
}

export default function Topbar({ theme, toggleTheme, serverUrl, updateServerUrl }) {
  const [isServerModalOpen, setIsServerModalOpen] = useState(false)
  const [draftServerUrl, setDraftServerUrl] = useState(serverUrl || '')

  useEffect(() => {
    if (isServerModalOpen) {
      setDraftServerUrl(serverUrl || '')
    }
  }, [isServerModalOpen, serverUrl])

  useEffect(() => {
    if (!isServerModalOpen) return undefined

    const handleKeyDown = (event) => {
      if (event.key === 'Escape') {
        setIsServerModalOpen(false)
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [isServerModalOpen])

  const trimmedDraftUrl = draftServerUrl.trim()
  const canSaveServerUrl = isValidServerUrl(trimmedDraftUrl)
  const effectiveServerLabel = useMemo(() => {
    return trimmedDraftUrl || window.location.origin
  }, [trimmedDraftUrl])

  const saveServerUrl = () => {
    if (!canSaveServerUrl) return
    updateServerUrl(trimmedDraftUrl)
    setIsServerModalOpen(false)
  }

  return (
    <>
      <header className="topbar">
        <div className="topbar-left">
          <span className="topbar-badge server-badge">
            <svg width="14" height="14" viewBox="0 0 20 20" fill="currentColor">
              <path fillRule="evenodd" d="M2 5a2 2 0 012-2h12a2 2 0 012 2v2a2 2 0 01-2 2H4a2 2 0 01-2-2V5zm14 1a1 1 0 11-2 0 1 1 0 012 0zM2 13a2 2 0 012-2h12a2 2 0 012 2v2a2 2 0 01-2 2H4a2 2 0 01-2-2v-2zm14 1a1 1 0 11-2 0 1 1 0 012 0z" clipRule="evenodd" />
            </svg>
            {serverUrl || window.location.origin}
          </span>
        </div>
        <div className="topbar-right">
          <button
            className="theme-toggle"
            onClick={() => setIsServerModalOpen(true)}
            title="Change server endpoint"
          >
            <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor">
              <path fillRule="evenodd" d="M3 4.75A1.75 1.75 0 014.75 3h6.5A1.75 1.75 0 0113 4.75v1.5a.75.75 0 01-1.5 0v-1.5a.25.25 0 00-.25-.25h-6.5a.25.25 0 00-.25.25v10.5c0 .138.112.25.25.25h6.5a.25.25 0 00.25-.25v-1.5a.75.75 0 011.5 0v1.5A1.75 1.75 0 0111.25 17h-6.5A1.75 1.75 0 013 15.25V4.75z" clipRule="evenodd" />
              <path fillRule="evenodd" d="M12.22 6.97a.75.75 0 011.06 0l2.5 2.5a.75.75 0 010 1.06l-2.5 2.5a.75.75 0 11-1.06-1.06L13.44 10H8.5a.75.75 0 010-1.5h4.94l-1.22-1.22a.75.75 0 010-1.06z" clipRule="evenodd" />
            </svg>
          </button>
          <a className="topbar-github" href="https://discord.gg/tRAN8HgvK5" target="_blank" rel="noopener noreferrer" title="Discord">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor">
              <path d="M20.317 4.37a19.791 19.791 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.21.375-.444.864-.608 1.25a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.617-1.25.077.077 0 0 0-.079-.037A19.736 19.736 0 0 0 3.677 4.37a.07.07 0 0 0-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 0 0 .031.057 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0 .084-.028c.462-.63.874-1.295 1.226-1.994a.076.076 0 0 0-.041-.106 13.107 13.107 0 0 1-1.872-.892.077.077 0 0 1-.008-.128 10.2 10.2 0 0 0 .372-.292.074.074 0 0 1 .077-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 0 1 .078.01c.12.098.246.198.373.292a.077.077 0 0 1-.006.127 12.299 12.299 0 0 1-1.873.892.077.077 0 0 0-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 0 0 .084.028 19.839 19.839 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 0 0-.031-.03zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z" />
            </svg>
          </a>
          <a className="topbar-github" href="https://github.com/jchristn/crawlsharp" target="_blank" rel="noopener noreferrer" title="GitHub">
            <svg width="18" height="18" viewBox="0 0 16 16" fill="currentColor">
              <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z" />
            </svg>
          </a>
          <button className="theme-toggle" onClick={toggleTheme} title={theme === 'light' ? 'Switch to dark mode' : 'Switch to light mode'}>
            {theme === 'light' ? (
              <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor">
                <path d="M17.293 13.293A8 8 0 016.707 2.707a8.001 8.001 0 1010.586 10.586z" />
              </svg>
            ) : (
              <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor">
                <path fillRule="evenodd" d="M10 2a1 1 0 011 1v1a1 1 0 11-2 0V3a1 1 0 011-1zm4 8a4 4 0 11-8 0 4 4 0 018 0zm-.464 4.95l.707.707a1 1 0 001.414-1.414l-.707-.707a1 1 0 00-1.414 1.414zm2.12-10.607a1 1 0 010 1.414l-.706.707a1 1 0 11-1.414-1.414l.707-.707a1 1 0 011.414 0zM17 11a1 1 0 100-2h-1a1 1 0 100 2h1zm-7 4a1 1 0 011 1v1a1 1 0 11-2 0v-1a1 1 0 011-1zM5.05 6.464A1 1 0 106.465 5.05l-.708-.707a1 1 0 00-1.414 1.414l.707.707zm1.414 8.486l-.707.707a1 1 0 01-1.414-1.414l.707-.707a1 1 0 011.414 1.414zM4 11a1 1 0 100-2H3a1 1 0 000 2h1z" clipRule="evenodd" />
              </svg>
            )}
          </button>
        </div>
      </header>

      {isServerModalOpen && (
        <div className="modal-overlay" onClick={() => setIsServerModalOpen(false)}>
          <div className="modal" onClick={event => event.stopPropagation()}>
            <h2>Server Endpoint</h2>
            <p className="modal-description">
              Choose where the dashboard sends crawl requests. Leave the URL empty to use the dashboard origin or reverse proxy.
            </p>

            <div className="server-endpoint-grid">
              <button
                type="button"
                className={`server-endpoint-option ${trimmedDraftUrl === '' ? 'active' : ''}`}
                onClick={() => setDraftServerUrl('')}
              >
                <span className="server-endpoint-option-title">Dashboard Origin / Proxy</span>
                <span className="server-endpoint-option-value">{window.location.origin}</span>
              </button>
              <button
                type="button"
                className={`server-endpoint-option ${trimmedDraftUrl === 'http://localhost:8000' ? 'active' : ''}`}
                onClick={() => setDraftServerUrl('http://localhost:8000')}
              >
                <span className="server-endpoint-option-title">Localhost</span>
                <span className="server-endpoint-option-value">http://localhost:8000</span>
              </button>
            </div>

            <div className="form-group">
              <label>Custom Server URL</label>
              <input
                type="url"
                value={draftServerUrl}
                onChange={event => setDraftServerUrl(event.target.value)}
                placeholder="https://crawlsharp.example.com"
              />
              <p className="form-hint">Use an absolute `http://` or `https://` URL, or leave this blank to route through the dashboard.</p>
              {!canSaveServerUrl && (
                <p className="form-hint server-endpoint-error">Enter a valid `http://` or `https://` server URL.</p>
              )}
            </div>

            <div className="server-endpoint-preview">
              <span className="server-endpoint-preview-label">Current selection</span>
              <span className="server-endpoint-preview-value">{effectiveServerLabel}</span>
            </div>

            <div className="modal-actions">
              <button type="button" className="btn btn-secondary" onClick={() => setIsServerModalOpen(false)}>
                Cancel
              </button>
              <button type="button" className="btn btn-primary" onClick={saveServerUrl} disabled={!canSaveServerUrl}>
                Save Endpoint
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
