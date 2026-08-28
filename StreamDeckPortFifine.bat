@ECHO OFF
SETLOCAL
SET "SCRIPT=%~dp0scripts\Install-PortFifine.ps1"
SET "NOPAUSE=0"
FOR %%A IN (%*) DO (
    IF /I "%%~A"=="-NoPause" SET "NOPAUSE=1"
    IF /I "%%~A"=="--NoPause" SET "NOPAUSE=1"
    IF /I "%%~A"=="/NoPause" SET "NOPAUSE=1"
)

IF NOT EXIST "%SCRIPT%" (
    ECHO ERRO: script do instalador nao encontrado em:
    ECHO   %SCRIPT%
    ECHO Verifique se scripts\Install-PortFifine.ps1 esta ao lado deste launcher.
    SET "EXITCODE=1"
    GOTO :PAUSE_EXIT
)

powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%SCRIPT%" %*
SET "EXITCODE=%ERRORLEVEL%"

:PAUSE_EXIT
IF "%NOPAUSE%"=="0" (
    ECHO.
    PAUSE
)
EXIT /B %EXITCODE%
