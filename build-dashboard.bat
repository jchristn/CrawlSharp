@ECHO OFF
SETLOCAL
IF "%~1" == "" GOTO :Usage
SET TAG=%~1
ECHO.
ECHO Building CrawlSharp Dashboard for linux/amd64 and linux/arm64/v8...
REM Single cloud build pushed to Docker Hub. The cloud builder cannot export a
REM multi-node result to the local store, so we pull the pushed image afterward
REM to land it in the local image store as well (cloud builder is hit only once).
pushd "%~dp0"
docker buildx build --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/crawlsharp-ui:%TAG% --tag jchristn77/crawlsharp-ui:latest --push dashboard\
SET EXIT_CODE=%ERRORLEVEL%
popd
IF NOT "%EXIT_CODE%" == "0" GOTO :Done

ECHO.
ECHO Pulling pushed image into the local image store...
docker pull jchristn77/crawlsharp-ui:%TAG%
docker pull jchristn77/crawlsharp-ui:latest

GOTO :Done

:Usage
ECHO.
ECHO Provide an argument with the tag for the build.
ECHO Example: build-dashboard.bat v1.0.0
SET EXIT_CODE=1

:Done
ECHO.
ECHO Done
ENDLOCAL & EXIT /B %EXIT_CODE%
