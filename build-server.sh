#!/usr/bin/env bash
# Builds and pushes the CrawlSharp Server image for linux/amd64 and linux/arm64/v8 with the given tag (plus :latest)
# on the cloud-jchristn77-jchristn77 builder, then pulls it into the local image store.
# Single cloud build pushed to Docker Hub. The cloud builder cannot export a multi-node result to the local store,
# so the pushed image is pulled afterward (the cloud builder is hit only once).
set -u

if [ $# -lt 1 ] || [ -z "$1" ]; then
  echo
  echo "Provide an argument with the tag for the build."
  echo "Example: ./build-server.sh v1.0.0"
  exit 1
fi

TAG="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo
echo "Building CrawlSharp Server for linux/amd64 and linux/arm64/v8..."
pushd "$SCRIPT_DIR/src" > /dev/null
docker buildx build -f CrawlSharp.Server/Dockerfile --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag "jchristn77/crawlsharp:$TAG" --tag jchristn77/crawlsharp:latest --push .
EXIT_CODE=$?
popd > /dev/null

if [ $EXIT_CODE -eq 0 ]; then
  echo
  echo "Pulling pushed image into the local image store..."
  docker pull "jchristn77/crawlsharp:$TAG"
  docker pull jchristn77/crawlsharp:latest
fi

echo
echo "Done"
exit $EXIT_CODE
