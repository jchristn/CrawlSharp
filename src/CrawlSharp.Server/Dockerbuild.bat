@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building for linux/amd64 and linux/arm64/v8...
docker buildx build -f Dockerfile --platform linux/amd64,linux/arm64/v8 --tag jchristn/crawlsharp:%1 --push .

GOTO :Done

:Usage
ECHO.
ECHO Provide an argument with the tag for the build.
ECHO Example: dockerbuild.bat v1.0.0

:Done
ECHO.
ECHO Done
@ECHO ON
