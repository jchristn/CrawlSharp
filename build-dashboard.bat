@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building CrawlSharp Dashboard for linux/amd64 and linux/arm64/v8...
docker buildx build --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/crawlsharp-ui:%1 --tag jchristn77/crawlsharp-ui:latest --push dashboard\

GOTO :Done

:Usage
ECHO.
ECHO Provide an argument with the tag for the build.
ECHO Example: build-dashboard.bat v1.0.0

:Done
ECHO.
ECHO Done
@ECHO ON
