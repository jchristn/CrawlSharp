import React, { useState } from 'react'

// Bundled observability tools from the compose stack. URLs are the browser-reachable, host-published addresses and
// come from runtime config (docker-entrypoint.sh writes them from environment variables). A service whose URL is
// empty is not part of this deployment and is omitted. Credentials are local-development defaults; see TELEMETRY.md.
const SERVICES = [
  {
    key: 'GRAFANA_URL',
    name: 'Grafana',
    fallback: 'http://localhost:3000',
    description: 'Dashboards in the CrawlSharp folder: Overview, HTTP, Crawl Pipeline, Integrations, Runtime, Logs and Traces.',
    credentials: 'admin / admin'
  },
  {
    key: 'PROMETHEUS_URL',
    name: 'Prometheus',
    fallback: 'http://localhost:9090',
    description: 'Metrics store and PromQL query console.',
    credentials: null
  },
  {
    key: 'TEMPO_URL',
    name: 'Tempo',
    fallback: 'http://localhost:3200',
    description: 'Trace store API. Browse traces through Grafana Explore.',
    credentials: null
  },
  {
    key: 'LOKI_URL',
    name: 'Loki',
    fallback: 'http://localhost:3100',
    description: 'Log store API. Browse logs through Grafana Explore.',
    credentials: null
  }
]

function resolveServices() {
  const config = window.__CRAWLSHARP_CONFIG__ || {}
  return SERVICES
    .map(s => ({ ...s, url: (config[s.key] ?? s.fallback ?? '').trim() }))
    .filter(s => s.url.length > 0)
}

function CopyButton({ value, label }) {
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(value)
      } else {
        const area = document.createElement('textarea')
        area.value = value
        area.style.position = 'fixed'
        area.style.opacity = '0'
        document.body.appendChild(area)
        area.select()
        document.execCommand('copy')
        document.body.removeChild(area)
      }
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      setCopied(false)
    }
  }

  return (
    <button className="btn btn-sm btn-ghost" onClick={copy} title={`Copy ${label}`} aria-label={`Copy ${label}`}>
      {copied ? 'Copied' : 'Copy'}
    </button>
  )
}

export default function ExternalServicesCard() {
  const services = resolveServices()

  return (
    <div className="card">
      <div className="card-header">
        <h2>External Services</h2>
      </div>
      {services.length === 0 ? (
        <div className="empty-state">
          <h3>No external services configured</h3>
          <p>This deployment does not include the bundled observability stack. See TELEMETRY.md to enable it.</p>
        </div>
      ) : (
        <div className="data-table-wrapper">
          <table>
            <thead>
              <tr>
                <th>Service</th>
                <th>URL</th>
                <th>Credentials</th>
                <th>Purpose</th>
              </tr>
            </thead>
            <tbody>
              {services.map(s => (
                <tr key={s.key}>
                  <td><strong>{s.name}</strong></td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <a className="mono" href={s.url} target="_blank" rel="noopener noreferrer">{s.url}</a>
                      <CopyButton value={s.url} label={`${s.name} URL`} />
                    </div>
                  </td>
                  <td>
                    {s.credentials ? (
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <span className="mono">{s.credentials}</span>
                        <CopyButton value={s.credentials} label={`${s.name} credentials`} />
                      </div>
                    ) : (
                      <span className="text-muted">None</span>
                    )}
                  </td>
                  <td className="text-muted text-sm">{s.description}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <div className="stat-detail" style={{ marginTop: 12 }}>
        Default credentials are for local development only. Change them before sharing or hosting this stack.
      </div>
    </div>
  )
}
