if [ -z "${IMG_TAG}" ]; then
  IMG_TAG='v1.0.0'
fi

echo Using image tag $IMG_TAG

docker run \
  -p 8000:8000 \
  -t \
  -i \
  -e "TERM=xterm-256color" \
  jchristn/crawlsharp:$IMG_TAG

