@echo off
rem One-command dev startup: backend API + Swagger + Angular admin dev server.
rem See start-dev.ps1 for options (e.g. -SkipBrowserOpen).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-dev.ps1" %*
