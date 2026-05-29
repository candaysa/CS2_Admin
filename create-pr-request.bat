@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "GIT=C:\Program Files\Git\cmd\git.exe"
if not exist "%GIT%" set "GIT=git"

for /f "delims=" %%i in ('"%GIT%" rev-parse --show-toplevel 2^>nul') do set "REPO_ROOT=%%i"
if not defined REPO_ROOT (
    echo Git repository bulunamadi.
    pause
    exit /b 1
)

cd /d "%REPO_ROOT%"

for /f "delims=" %%i in ('"%GIT%" rev-parse --abbrev-ref HEAD') do set "CURRENT_BRANCH=%%i"
for /f "delims=" %%i in ('"%GIT%" remote get-url origin') do set "REMOTE_URL=%%i"

if not defined CURRENT_BRANCH (
    echo Mevcut branch alinmadi.
    pause
    exit /b 1
)

if not defined REMOTE_URL (
    echo origin remote bulunamadi.
    pause
    exit /b 1
)

set "REPO_URL=%REMOTE_URL%"
if /I "!REPO_URL:~-4!"==".git" set "REPO_URL=!REPO_URL:~0,-4!"

if /I "%CURRENT_BRANCH%"=="main" goto :choose_branch
if /I "%CURRENT_BRANCH%"=="master" goto :choose_branch
goto :status_check

:choose_branch
echo Su an %CURRENT_BRANCH% branch'indesin.
set /p "NEW_BRANCH=PR icin yeni branch adi gir (ornegin fix-chat-log): "
if not defined NEW_BRANCH (
    echo Branch adi bos birakilamaz.
    pause
    exit /b 1
)

echo %NEW_BRANCH%| findstr /b /c:"codex/" >nul
if errorlevel 1 (
    set "NEW_BRANCH=codex/%NEW_BRANCH%"
)

"%GIT%" checkout -b "%NEW_BRANCH%"
if errorlevel 1 (
    echo Yeni branch olusturulamadi.
    pause
    exit /b 1
)

set "CURRENT_BRANCH=%NEW_BRANCH%"

:status_check
for /f "delims=" %%i in ('"%GIT%" status --porcelain') do set "HAS_CHANGES=1"
if defined HAS_CHANGES (
    echo Uyari: Commitlenmemis degisiklikler var. Push sadece commitli degisiklikleri gonderir.
    choice /M "Yine de devam etmek istiyor musun"
    if errorlevel 2 exit /b 1
)

echo.
echo Branch push ediliyor: %CURRENT_BRANCH%
"%GIT%" push -u origin "%CURRENT_BRANCH%"
if errorlevel 1 (
    echo Push basarisiz oldu.
    pause
    exit /b 1
)

set "BASE_BRANCH=main"
set "PR_URL=%REPO_URL%/compare/%BASE_BRANCH%...%CURRENT_BRANCH%?expand=1"

echo.
echo PR ekrani aciliyor:
echo %PR_URL%
start "" "%PR_URL%"

echo.
echo Tamamlandi.
pause
exit /b 0
