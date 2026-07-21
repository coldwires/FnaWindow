@echo off
rem Run this once after cloning on a new machine. It does what a clone cannot do for itself:
rem
rem   1. fetches the engine - it is a submodule, so a clone brings only a pointer to it and the
rem      engine\ folder arrives empty
rem   2. if this repo ships git hooks in .githooks, points git at them - git will not enable a
rem      repo's own hooks by itself, because cloning a repo should never run its code
rem
rem Everything above the "app-specific" line further down is identical in every app repo. It cannot
rem live in the engine and be shared, because fetching the engine is the job it exists to do. If you
rem change that shared part, change it everywhere.
setlocal
cd /d "%~dp0"

git rev-parse --git-dir >nul 2>&1
if errorlevel 1 (
    echo This folder is not a git checkout, so there is nothing to set up.
    exit /b 1
)

echo Fetching the engine submodule...
git submodule update --init --recursive
if errorlevel 1 (
    echo.
    echo FAILED to fetch the engine. Without it there is no engine\FnaWindow.csproj and nothing
    echo will build. Check the network, and that this machine can reach the FnaWindow repository.
    exit /b 1
)

set HOOKS=none
if exist ".githooks" (
    echo Enabling the tracked git hooks...
    git config core.hooksPath .githooks
    if errorlevel 1 (
        echo Could not set core.hooksPath. The app still builds; only the hooks are off.
    ) else (
        set HOOKS=.githooks
    )
)

echo.
if not exist "engine\FnaWindow.csproj" (
    echo Setup ran, but engine\FnaWindow.csproj is still missing, so the build will fail.
    exit /b 1
)

echo Setup done.
echo   engine       present
echo   git hooks    %HOOKS%
echo.
echo Next: dotnet build, then dotnet run
