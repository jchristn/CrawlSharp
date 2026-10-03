#!/usr/bin/env bash
# Builds and pushes every CrawlSharp image with the given tag (plus :latest) on the
# cloud-jchristn77-jchristn77 builder, then pulls each into the local image store.
# Stops at the first build that fails.
set -u

if [ $# -lt 1 ] || [ -z "$1" ]; then
  echo
  echo "Provide an argument with the tag for the build."
  echo "Example: ./build-all.sh v1.0.0"
  exit 1
fi

TAG="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"$SCRIPT_DIR/build-server.sh" "$TAG"
EXIT_CODE=$?
if [ $EXIT_CODE -ne 0 ]; then
  echo
  echo "Build failed with exit code $EXIT_CODE; remaining images were not built."
  exit $EXIT_CODE
fi

"$SCRIPT_DIR/build-dashboard.sh" "$TAG"
EXIT_CODE=$?
if [ $EXIT_CODE -ne 0 ]; then
  echo
  echo "Build failed with exit code $EXIT_CODE; remaining images were not built."
  exit $EXIT_CODE
fi

echo
echo "All images built and pushed with tag $TAG."
exit 0
