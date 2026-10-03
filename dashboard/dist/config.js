// Runtime configuration - overridden by docker-entrypoint.sh in production
window.__CRAWLSHARP_CONFIG__ = {
  CRAWLSHARP_SERVER_URL: "http://localhost:8000",
  GRAFANA_URL: "http://localhost:3000",
  PROMETHEUS_URL: "http://localhost:9090",
  TEMPO_URL: "http://localhost:3200",
  LOKI_URL: "http://localhost:3100"
};
