@echo off

IF "%1" == "" GOTO :Usage

docker run ^
  -p 8000:8000 ^
  -t ^
  -i ^
  -e "TERM=xterm-256color" ^
  jchristn/crawlsharp:%1

GOTO :Done

:Usage
ECHO Provide one argument indicating the tag. 
ECHO Example: dockerrun.bat v1.0.0
:Done
@echo on
