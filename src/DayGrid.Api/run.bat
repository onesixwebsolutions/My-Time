@echo off
REM Launcher for the published DayGrid.Api.exe — double-click this instead of the exe directly.
REM
REM By default the app provisions its own private Postgres instance on first launch (see
REM EmbeddedDatabase.cs) — no Postgres install required on this machine, nothing else on it is
REM touched. That first launch needs internet access once, to download the Postgres binaries;
REM every launch after that reuses the already-downloaded copy and the data already created
REM under %LocalAppData%\DayGrid\pgdata, so your data survives restarts normally.
REM
REM This script's only real job is giving you a predictable URL instead of a random port.
setlocal
set "SCRIPT_DIR=%~dp0"
set "ASPNETCORE_URLS=http://localhost:5080"
REM Base URL used in account emails (email confirmation / password reset links).
set "App__PublicBaseUrl=http://localhost:5080"

REM --- Optional: point at a real Postgres instance instead of the built-in one ---
REM Uncomment both lines below and fill in your own server to skip the embedded database
REM entirely (e.g. you already run Postgres, or want this pointed at a shared/remote one).
REM set "Database__Mode=External"
REM set "ConnectionStrings__Default=Host=localhost;Database=daygrid;Username=daygrid;Password=dev"

"%SCRIPT_DIR%DayGrid.Api.exe"

echo.
echo DayGrid has stopped. Press any key to close this window.
pause >nul
