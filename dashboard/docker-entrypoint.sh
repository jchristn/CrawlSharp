#!/bin/sh
# Generate runtime config from environment variables.
# An observability URL set to an empty string hides that service on the External Services card.
cat <<EOF2 > /usr/share/nginx/html/config.js
window.__CRAWLSHARP_CONFIG__ = {
  CRAWLSHARP_SERVER_URL: "${CRAWLSHARP_SERVER_URL-http://localhost:8000}",
  GRAFANA_URL: "${GRAFANA_URL-http://localhost:3000}",
  PROMETHEUS_URL: "${PROMETHEUS_URL-http://localhost:9090}",
  TEMPO_URL: "${TEMPO_URL-http://localhost:3200}",
  LOKI_URL: "${LOKI_URL-http://localhost:3100}"
};
EOF2

exec "$@"
