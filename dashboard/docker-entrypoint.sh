#!/bin/sh
# Generate runtime config from environment variables
cat <<EOF > /usr/share/nginx/html/config.js
window.__CRAWLSHARP_CONFIG__ = {
  CRAWLSHARP_SERVER_URL: "${CRAWLSHARP_SERVER_URL-http://localhost:8000}"
};
EOF

exec "$@"
