@ECHO OFF
SETLOCAL
IF "%~1" == "" GOTO :Usage
SET TAG=%~1
REM Builds and pushes every CrawlSharp image with the given tag (plus :latest) on the
REM cloud-jchristn77-jchristn77 builder, then pulls each into the local image store.
REM Stops at the first build that fails.

pushd "%~dp0"

CALL "%~dp0build-server.bat" "%TAG%"
SET EXIT_CODE=%ERRORLEVEL%
IF NOT "%EXIT_CODE%" == "0" GOTO :Failed

CALL "%~dp0build-dashboard.bat" "%TAG%"
SET EXIT_CODE=%ERRORLEVEL%
IF NOT "%EXIT_CODE%" == "0" GOTO :Failed

popd
ECHO.
ECHO All images built and pushed with tag %TAG%.
ENDLOCAL & EXIT /B 0

:Failed
popd
ECHO.
ECHO Build failed with exit code %EXIT_CODE%; remaining images were not built.
ENDLOCAL & EXIT /B %EXIT_CODE%

:Usage
ECHO.
ECHO Provide an argument with the tag for the build.
ECHO Example: build-all.bat v1.0.0
ENDLOCAL & EXIT /B 1
