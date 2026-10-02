@echo off
cd /d "%~dp0..\AssetBundles"

where python >nul 2>nul
if %errorlevel%==0 (
    python -m http.server 8000
) else (
    py -m http.server 8000
)
