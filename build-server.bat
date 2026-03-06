@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building CrawlSharp Server for linux/amd64 and linux/arm64/v8...
pushd src
docker buildx build -f CrawlSharp.Server\Dockerfile --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/crawlsharp:%1 --tag jchristn77/crawlsharp:latest --push .
popd

GOTO :Done

:Usage
ECHO.
ECHO Provide an argument with the tag for the build.
ECHO Example: build-server.bat v1.0.0

:Done
ECHO.
ECHO Done
@ECHO ON
