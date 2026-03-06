import React, { useState, useEffect } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import Sidebar from './components/Sidebar.jsx'
import Topbar from './components/Topbar.jsx'
import DashboardView from './views/DashboardView.jsx'
import NewCrawlView from './views/NewCrawlView.jsx'
import ActiveCrawlView from './views/ActiveCrawlView.jsx'
import CrawlHistoryView from './views/CrawlHistoryView.jsx'
import CrawlResultsView from './views/CrawlResultsView.jsx'
import TemplatesView from './views/TemplatesView.jsx'

export default function App() {
  const [theme, setTheme] = useState(() => localStorage.getItem('crawlsharp_theme') || 'dark')
  const [serverUrl, setServerUrl] = useState(() => {
    return window.__CRAWLSHARP_CONFIG__?.CRAWLSHARP_SERVER_URL ??
      localStorage.getItem('crawlsharp_server_url') ??
      'http://localhost:8000'
  })

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
    localStorage.setItem('crawlsharp_theme', theme)
  }, [theme])

  const toggleTheme = () => setTheme(t => t === 'light' ? 'dark' : 'light')

  const updateServerUrl = (url) => {
    const cleaned = url.replace(/\/+$/, '')
    setServerUrl(cleaned)
    localStorage.setItem('crawlsharp_server_url', cleaned)
  }

  return (
    <BrowserRouter>
      <div className="app-layout">
        <Sidebar />
        <div className="main-wrapper">
          <Topbar theme={theme} toggleTheme={toggleTheme} serverUrl={serverUrl} />
          <main className="main-content">
            <Routes>
              <Route path="/" element={<DashboardView serverUrl={serverUrl} />} />
              <Route path="/crawl/new" element={<NewCrawlView serverUrl={serverUrl} />} />
              <Route path="/crawl/active/:id" element={<ActiveCrawlView serverUrl={serverUrl} />} />
              <Route path="/history" element={<CrawlHistoryView serverUrl={serverUrl} />} />
              <Route path="/history/:id" element={<CrawlResultsView serverUrl={serverUrl} />} />
              <Route path="/templates" element={<TemplatesView />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </main>
        </div>
      </div>
    </BrowserRouter>
  )
}
