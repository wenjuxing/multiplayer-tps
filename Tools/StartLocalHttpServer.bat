@rem 在本地启动资源包 HTTP 服务
@echo off
rem 将资源包输出目录作为 HTTP 服务根目录
cd /d "%~dp0..\AssetBundles"

rem 优先通过 python 命令监听 8000 端口，其次使用 py 启动器
where python >nul 2>nul
if %errorlevel%==0 (
    python -m http.server 8000
) else (
    py -m http.server 8000
)
