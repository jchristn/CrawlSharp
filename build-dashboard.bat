@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building CrawlSharp Dashboard for linux/amd64 and linux/arm64/v8...
REM Single cloud build pushed to Docker Hub. The cloud builder cannot export a
REM multi-node result to the local store, so we pull the pushed image afterward
REM to land it in the local image store as well (cloud builder is hit only once).
docker buildx build --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/crawlsharp-ui:%1 --tag jchristn77/crawlsharp-ui:latest --push dashboard\
IF ERRORLEVEL 1 GOTO :Done

ECHO.
ECHO Pulling pushed image into the local image store...
docker pull jchristn77/crawlsharp-ui:%1
docker pull jchristn77/crawlsharp-ui:latest

GOTO :Done

:Usage
ECHO.
ECHO Provide an argument with the tag for the build.
ECHO Example: build-dashboard.bat v1.0.0

:Done
ECHO.
ECHO Done
@ECHO ON
