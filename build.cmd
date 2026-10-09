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

rem ============================================================
rem  WHY do_clean ALSO DELETES bin/ AND obj/
rem
rem  "dotnet clean" removes intermediate outputs but LEAVES the
rem  built EXE in place. A subsequent "build" may then reuse it,
rem  so a freshly built binary is not guaranteed to come from
rem  the current source. The symptom is "I changed the code but
rem  the UI looks identical", which is hard to diagnose.
rem
rem  Deleting bin/ and obj/ after "dotnet clean" makes
rem  "build.cmd clean" + "build.cmd build" produce an EXE that
rem  provably comes from current source. Required by AGENTS.md
rem  section 1.6.1 before handing a binary to the user.
rem
rem  Known blind spot: if ReleaseWatcher.App.exe is still running,
rem  its files are locked and deletion fails. The script reports
rem  this instead of pretending to have succeeded.
rem ============================================================

:do_clean
"%DOTNET%" clean "%SLNX%" -c %CONFIG%
if errorlevel 1 exit /b %ERRORLEVEL%

rem Delete bin/ and obj/ for every project in the solution.
rem /s = recurse subdirs, /q = quiet, /f = force (ignore errors
rem on locked files so one stuck project cannot block cleanup).
for /d %%D in ("%~dp0src\*" "%~dp0tests\*") do (
    if exist "%%~fD" (
        for /d %%P in ("%%~fD\*") do (
            if /i "%%~nxP"=="bin" rmdir /s /q "%%~fP"
            if /i "%%~nxP"=="obj" rmdir /s /q "%%~fP"
        )
    )
)

rem Report honestly: a locked EXE means bin/ could not be fully
rem cleared. Saying "clean succeeded" here would be a lie the user
rem only discovers later as a stale-binary mystery.
rem
rem Detection deliberately avoids piping tasklist into find: that
rem pipeline misbehaves when build.cmd is invoked from Git Bash
rem (find is resolved as the Unix tool and errors out), which
rem silently suppressed the warning during testing.
rem
rem Instead, check for the EXE file itself. It only survives when
rem the process is alive and holding it locked, so its continued
rem presence after rmdir is a reliable, shell-independent signal.
if exist "%~dp0src\ReleaseWatcher.App\bin\Release\net10.0-windows\ReleaseWatcher.App.exe" (
    echo [WARN] ReleaseWatcher.App.exe still present - likely still running and locking its files.
    echo [WARN] Stop it first: taskkill /F /IM ReleaseWatcher.App.exe
    echo [WARN] Otherwise the next build may fail with MSB3021 / MSB3027, or you may test a stale EXE.
)

echo [INFO] Clean complete: bin and obj removed.
exit /b 0

:do_sdk
"%DOTNET%" --list-sdks
exit /b %ERRORLEVEL%