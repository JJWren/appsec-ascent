@echo off
rem Runs the AppSec Ascent Engine from source (until the first NuGet release makes `dotnet tool restore` available).
rem It builds once into .ascent\bin and rebuilds only when the engine's sources change (PERF-U2-01).
rem `ascent --rebuild` forces a build and then exits.
setlocal
set "HERE=%~dp0"
set "BIN=%HERE%.ascent\bin"
set "DLL=%BIN%\ascent.dll"
set "STAMPFILE=%BIN%\.stamp"
set "NEEDS_BUILD="
set "REBUILD_ONLY="
if /i "%~1"=="--rebuild" (
  set "NEEDS_BUILD=1"
  set "REBUILD_ONLY=1"
)

set "STAMP="
set "DIRTY="
git -C "%HERE%." rev-parse --is-inside-work-tree >nul 2>&1
if not errorlevel 1 (
  for /f "delims=" %%L in ('git -C "%HERE%." status --porcelain -- engine') do set "DIRTY=1"
  if not defined DIRTY for /f "delims=" %%S in ('git -C "%HERE%." rev-parse HEAD:engine') do set "STAMP=%%S"
)
if defined DIRTY set "NEEDS_BUILD=1"

set "OLDSTAMP="
if exist "%STAMPFILE%" set /p OLDSTAMP=<"%STAMPFILE%"
if not exist "%DLL%" set "NEEDS_BUILD=1"
if defined STAMP if not "%STAMP%"=="%OLDSTAMP%" set "NEEDS_BUILD=1"

if defined NEEDS_BUILD (
  dotnet build "%HERE%engine\src\Ascent.Cli\Ascent.Cli.csproj" --configuration Release --output "%BIN%" --nologo --verbosity quiet -consoleLoggerParameters:NoSummary 1>&2
  if errorlevel 1 exit /b 1
  >"%STAMPFILE%" <nul set /p "=%STAMP%"
)

if defined REBUILD_ONLY exit /b 0
dotnet "%DLL%" %*
exit /b %ERRORLEVEL%
