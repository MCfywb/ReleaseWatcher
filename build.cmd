@echo off
setlocal

rem ============================================================
rem  ReleaseWatcher build script
rem
rem  WHY THIS FILE EXISTS
rem    The machine PATH resolves dotnet.exe to
rem    "C:\Program Files (x86)\dotnet\dotnet.exe", which has NO sdk
rem    directory. Invoking plain "dotnet" there fails with
rem    "No .NET SDKs were found".
rem    The real SDK lives in "C:\Program Files\dotnet" (10.0.401).
rem    This script therefore uses an absolute path and does NOT
rem    modify the system PATH. See RUNBOOK.md section 1.
rem
rem  NOTE: keep this file ASCII-only. cmd.exe parses it using the
rem    active OEM code page; non-ASCII characters break parsing.
rem ============================================================

set "DOTNET=C:\Program Files\dotnet\dotnet.exe"

if not exist "%DOTNET%" (
    echo [ERROR] dotnet not found at: "%DOTNET%"
    echo Update the DOTNET path in this script. See RUNBOOK.md section 1.
    exit /b 1
)

set "SLNX=%~dp0ReleaseWatcher.slnx"
set "CONFIG=Release"

rem IMPORTANT: each subcommand jumps to a label instead of living
rem inside an "if (...)" block. In a parenthesized block cmd.exe expands
rem %ERRORLEVEL% at PARSE time, i.e. BEFORE dotnet runs, so it would
rem always yield the stale value 0 and hide build failures. A test run
rem that failed to compile would still report success. Do not convert
rem these back to if-blocks. Guarded by RepositoryConventionTests.

if /i "%1"=="build"    goto do_build
if /i "%1"=="test"     goto do_test
if /i "%1"=="clean"    goto do_clean
if /i "%1"=="sdk"      goto do_sdk

rem Publishing is a release step and requires explicit user
rem authorization per AGENTS.md section 1.5. Intentionally
rem not implemented here. See RUNBOOK.md section 5.
if /i "%1"=="publish" (
    echo [WARN] publish is a release step and requires explicit user authorization.
    echo See AGENTS.md section 1.5 and RUNBOOK.md section 5.
    exit /b 1
)

echo [INFO] ReleaseWatcher build script
echo.
echo   build.cmd build    Build Release configuration
echo   build.cmd test     Build and run tests
echo   build.cmd clean    Clean Release build artifacts
echo   build.cmd sdk      List available SDKs
echo.
echo dotnet : "%DOTNET%"
echo slnx   : "%SLNX%"
exit /b 0

:do_build
"%DOTNET%" build "%SLNX%" -c %CONFIG%
exit /b %ERRORLEVEL%

:do_test
"%DOTNET%" test "%SLNX%" -c %CONFIG%
exit /b %ERRORLEVEL%

:do_clean
"%DOTNET%" clean "%SLNX%" -c %CONFIG%
exit /b %ERRORLEVEL%

:do_sdk
"%DOTNET%" --list-sdks
exit /b %ERRORLEVEL%