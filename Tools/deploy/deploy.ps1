#Requires -Version 5.1
<#
.SYNOPSIS
    部署或回滚 FPSGame 的 Linux 专用服务端

.DESCRIPTION
    打包并上传构建产物，在远端解压、切换版本链接、重启服务并检查状态

    远端目录
        /opt/fpsgame                  指向当前运行版本的软链接
        /opt/fpsgame-20261003-1530    每个版本独立的目录
        /var/log/fpsgame/server.log   服务端日志

    回滚时复用历史版本目录并更新软链接

    此文件使用带 BOM 的 UTF-8，供 Windows PowerShell 5.1 正确读取中文注释

.EXAMPLE
    .\deploy.ps1                                  # 部署默认构建目录
    .\deploy.ps1 -InstallService                  # 安装或更新 fpsgame.service
    .\deploy.ps1 -Rollback                        # 回滚到历史版本
    .\deploy.ps1 -BuildDir "E:\...\Build\LinuxServer" -Keep 3
    .\deploy.ps1 -WithDebugSymbols                # 保留调试符号
#>
# 部署地址、构建输出、服务端口和回滚选项
[CmdletBinding()]
param(
    [string] $Server   = "43.142.103.57",
    [string] $User     = "ubuntu",
    [string] $KeyPath  = "$env:USERPROFILE\.ssh\G1.pem",
    [string] $BuildDir = "",
    [string] $ExeName  = "FPSGame-Y1.x86_64",
    [int]    $Port     = 7777,
    [int]    $Keep     = 5,
    [switch] $InstallService,
    [switch] $Rollback,
    [switch] $WithDebugSymbols,
    [switch] $SkipHealthCheck
)

Set-StrictMode -Version Latest

# 允许外部工具输出到错误流，命令是否失败由退出码判断
$ErrorActionPreference = "Continue"

# 以 UTF-8 显示远端服务端输出
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# 查找构建目录并检查本地部署工具、密钥和脚本
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($BuildDir)) { $BuildDir = Join-Path $repoRoot "Build\LinuxServer" }

$remoteShLocal = Join-Path $PSScriptRoot "deploy-remote.sh"
$unitFileLocal = Join-Path $PSScriptRoot "fpsgame.service"

foreach ($cmd in @("ssh", "scp", "tar")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        throw "Command not found: $cmd (Windows needs the OpenSSH client; tar ships with Windows)"
    }
}
if (-not (Test-Path -LiteralPath $KeyPath))       { throw "Private key not found: $KeyPath" }
if (-not (Test-Path -LiteralPath $remoteShLocal)) { throw "Missing deploy script: $remoteShLocal" }

# 组装 SSH 目标和连接选项
$target = "$User@$Server"
$sshOpts = @(
    "-i", $KeyPath,
    "-o", "BatchMode=yes",
    "-o", "ConnectTimeout=10",
    "-o", "StrictHostKeyChecking=accept-new"
)
$remoteSh      = "/tmp/fpsgame-deploy.sh"
$remoteShClean = "/tmp/fpsgame-deploy.clean.sh"

# 执行远端命令并返回退出码，默认将非零退出码作为错误
function Invoke-Remote {
    param(
        [Parameter(Mandatory = $true)][string] $Command,
        [switch] $AllowFail
    )
    # 收集 SSH 输出并记录退出码
    $output = & ssh @sshOpts $target $Command 2>&1
    $code = $LASTEXITCODE
    if ($null -ne $output) { $output | ForEach-Object { Write-Host "    $_" } }
    if ($code -ne 0 -and -not $AllowFail) { throw "Remote command failed (exit $code)" }
    return $code
}

Write-Host "== target: $target ==" -ForegroundColor Cyan

# 先验证 SSH 连通性和登录权限
$probeCode = Invoke-Remote "echo connected" -AllowFail
if ($probeCode -ne 0) { throw "Cannot SSH to $target. Check key/network: ssh -i `"$KeyPath`" $target" }

# 每次部署先上传当前版本的远端脚本
Write-Host "-> uploading deploy-remote.sh"
& scp @sshOpts $remoteShLocal "${target}:$remoteSh" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Upload of deploy-remote.sh failed (exit $LASTEXITCODE)" }

# 转成 Linux 换行后再交给 Bash 执行
$runRemoteSh = "tr -d '\r' < $remoteSh > $remoteShClean && sudo -n bash $remoteShClean"

# 回滚模式直接调用远端脚本并结束
if ($Rollback) {
    Write-Host "-> rolling back to the previous release" -ForegroundColor Yellow
    $code = Invoke-Remote "$runRemoteSh --rollback" -AllowFail
    if ($code -ne 0) { throw "Rollback failed (exit $code)" }
    Write-Host "Rollback done." -ForegroundColor Green
    return
}

# 查找待部署的 Linux 服务端程序
if (-not (Test-Path -LiteralPath $BuildDir)) {
    throw "Build directory not found: $BuildDir (run Build Profiles -> Linux Server -> Build first)"
}

$exePath = Join-Path $BuildDir $ExeName
if (-not (Test-Path -LiteralPath $exePath)) {
    $cand = Get-ChildItem -LiteralPath $BuildDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*.x86_64" } | Select-Object -First 1
    if ($null -eq $cand) { throw "No $ExeName and no *.x86_64 inside $BuildDir" }
    $ExeName = $cand.Name
    $exePath = $cand.FullName
    Write-Host "-> auto-detected executable: $ExeName"
}

# 检查服务端运行需要的 Unity 数据目录
$dataDirName = ([IO.Path]::GetFileNameWithoutExtension($ExeName)) + "_Data"
if (-not (Test-Path -LiteralPath (Join-Path $BuildDir $dataDirName))) {
    Write-Warning "Missing $dataDirName - Unity needs this folder at runtime, the server may fail to start"
}

# 将服务端构建目录压缩成带时间戳的部署包
$stamp     = Get-Date -Format "yyyyMMdd-HHmmss"
$tmpTar    = Join-Path $env:TEMP "fpsgame-$stamp.tar.gz"
$remoteTar = "/tmp/fpsgame-$stamp.tar.gz"

# 默认排除调试符号以减小部署包
$tarArgs = @("-czf", $tmpTar)
if (-not $WithDebugSymbols) { $tarArgs += @("--exclude=*_s.debug", "--exclude=*.pdb") }
$tarArgs += @("-C", $BuildDir, ".")

Write-Host "-> packing (exclude debug symbols: $(-not $WithDebugSymbols))"
& tar @tarArgs
if ($LASTEXITCODE -ne 0) { throw "tar failed (exit $LASTEXITCODE)" }

$sizeMB = [math]::Round((Get-Item -LiteralPath $tmpTar).Length / 1MB, 1)
Write-Host "   package size: $sizeMB MB -> $tmpTar"

# 上传部署包，成功后删除本地临时文件
Write-Host "-> uploading package to $remoteTar"
& scp @sshOpts $tmpTar "${target}:$remoteTar"
if ($LASTEXITCODE -ne 0) { throw "Package upload failed (exit $LASTEXITCODE)" }
Remove-Item -LiteralPath $tmpTar -Force

# 首次部署或更新服务配置时上传 systemd 单元文件
$remoteUnit = "/tmp/fpsgame.service"
if ($InstallService) {
    if (-not (Test-Path -LiteralPath $unitFileLocal)) { throw "Missing $unitFileLocal" }
    Write-Host "-> uploading fpsgame.service"
    & scp @sshOpts $unitFileLocal "${target}:$remoteUnit"
    if ($LASTEXITCODE -ne 0) { throw "Unit upload failed (exit $LASTEXITCODE)" }
}

# 调用远端脚本解包、切换版本并重启服务
$remoteArgs = "--tar $remoteTar --exe $ExeName --port $Port --keep $Keep"
if ($InstallService) { $remoteArgs += " --unit $remoteUnit" }

Write-Host "-> remote deploy (extract / switch symlink / restart service)" -ForegroundColor Cyan
$code = Invoke-Remote "$runRemoteSh $remoteArgs" -AllowFail
if ($code -ne 0) {
    Write-Warning "Remote deploy failed (exit $code)."
    Write-Warning "If it said 'sudo: a password is required', $User has no passwordless sudo."
    Write-Warning "Manual fallback: log in and run:  sudo bash $remoteShClean $remoteArgs"
    throw "Deploy did not complete"
}

# 按配置检查服务状态、监听端口和最近日志
if (-not $SkipHealthCheck) {
    Write-Host "-> health check" -ForegroundColor Cyan
    $healthCmd = "systemctl is-active fpsgame; ss -lunp | grep ':$Port' || echo '(no UDP $Port listener seen)'; echo '--- tail of server.log ---'; tail -n 10 /var/log/fpsgame/server.log 2>/dev/null || true"
    Invoke-Remote $healthCmd -AllowFail | Out-Null
}

Write-Host ""
Write-Host "Deploy finished." -ForegroundColor Green
Write-Host "  current: /opt/fpsgame -> /opt/fpsgame-<timestamp>"
Write-Host "  logs:    ssh -i `"$KeyPath`" $target 'tail -f /var/log/fpsgame/server.log'"
Write-Host "  rollback: .\Tools\deploy\deploy.ps1 -Rollback"
