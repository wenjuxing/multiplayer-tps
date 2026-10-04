#Requires -Version 5.1
<#
.SYNOPSIS
    One-command deploy of the FPSGame Linux Dedicated Server to the Tencent Cloud Lighthouse host.

.DESCRIPTION
    pack locally -> upload -> extract to /opt/fpsgame-<timestamp> on the server
    -> switch the /opt/fpsgame symlink -> restart the systemd service
    -> health check -> prune old versions.

    Server layout:
        /opt/fpsgame                  symlink, always points at the current version
        /opt/fpsgame-20261003-1530    one directory per release
        /var/log/fpsgame/server.log   server log

    Rollback = point the symlink back at the previous directory. No re-upload needed.

    NOTE: this file is intentionally ASCII-only. Windows PowerShell 5.1 decodes
    .ps1 files using the system code page (GBK on Chinese Windows) unless the
    file has a UTF-8 BOM, which mangles non-ASCII literals and breaks parsing.

.EXAMPLE
    .\deploy.ps1                                  # deploy Build\LinuxServer
    .\deploy.ps1 -InstallService                  # first run: also install/refresh fpsgame.service
    .\deploy.ps1 -Rollback                        # roll back to the previous release
    .\deploy.ps1 -BuildDir "E:\...\Build\LinuxServer" -Keep 3
    .\deploy.ps1 -WithDebugSymbols                # keep *_s.debug (package is ~2x bigger)
#>
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

# Deliberately Continue, not Stop.
# ssh / scp / tar write progress and warnings to stderr (scp percentage, ssh
# known_hosts notice). Under Windows PowerShell 5.1 with ErrorActionPreference
# 'Stop' those are turned into terminating errors and abort the script.
# Every external command below is checked explicitly via $LASTEXITCODE.
$ErrorActionPreference = "Continue"

# Remote (Linux) output is UTF-8. Tell the console to decode it as UTF-8 so the
# Chinese messages printed by deploy-remote.sh are readable instead of mojibake.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# ------------------------------------------------------------- preflight
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

$target = "$User@$Server"
$sshOpts = @(
    "-i", $KeyPath,
    "-o", "BatchMode=yes",
    "-o", "ConnectTimeout=10",
    "-o", "StrictHostKeyChecking=accept-new"
)
$remoteSh      = "/tmp/fpsgame-deploy.sh"
$remoteShClean = "/tmp/fpsgame-deploy.clean.sh"

function Invoke-Remote {
    param(
        [Parameter(Mandatory = $true)][string] $Command,
        [switch] $AllowFail
    )
    $output = & ssh @sshOpts $target $Command 2>&1
    $code = $LASTEXITCODE
    if ($null -ne $output) { $output | ForEach-Object { Write-Host "    $_" } }
    if ($code -ne 0 -and -not $AllowFail) { throw "Remote command failed (exit $code)" }
    return $code
}

Write-Host "== target: $target ==" -ForegroundColor Cyan

# Probe first so a broken key/login fails fast instead of hanging
$probeCode = Invoke-Remote "echo connected" -AllowFail
if ($probeCode -ne 0) { throw "Cannot SSH to $target. Check key/network: ssh -i `"$KeyPath`" $target" }

# ------------------------------- refresh the remote script on every run
Write-Host "-> uploading deploy-remote.sh"
& scp @sshOpts $remoteShLocal "${target}:$remoteSh" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Upload of deploy-remote.sh failed (exit $LASTEXITCODE)" }

# Strip CR before running: the file may have CRLF, bash would fail with $'\r': command not found
$runRemoteSh = "tr -d '\r' < $remoteSh > $remoteShClean && sudo -n bash $remoteShClean"

# ---------------------------------------------------------------- rollback
if ($Rollback) {
    Write-Host "-> rolling back to the previous release" -ForegroundColor Yellow
    $code = Invoke-Remote "$runRemoteSh --rollback" -AllowFail
    if ($code -ne 0) { throw "Rollback failed (exit $code)" }
    Write-Host "Rollback done." -ForegroundColor Green
    return
}

# --------------------------------------------------------- locate build output
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

$dataDirName = ([IO.Path]::GetFileNameWithoutExtension($ExeName)) + "_Data"
if (-not (Test-Path -LiteralPath (Join-Path $BuildDir $dataDirName))) {
    Write-Warning "Missing $dataDirName - Unity needs this folder at runtime, the server may fail to start"
}

# ------------------------------------------------------------------- pack
$stamp     = Get-Date -Format "yyyyMMdd-HHmmss"
$tmpTar    = Join-Path $env:TEMP "fpsgame-$stamp.tar.gz"
$remoteTar = "/tmp/fpsgame-$stamp.tar.gz"

$tarArgs = @("-czf", $tmpTar)
if (-not $WithDebugSymbols) { $tarArgs += @("--exclude=*_s.debug", "--exclude=*.pdb") }
$tarArgs += @("-C", $BuildDir, ".")

Write-Host "-> packing (exclude debug symbols: $(-not $WithDebugSymbols))"
& tar @tarArgs
if ($LASTEXITCODE -ne 0) { throw "tar failed (exit $LASTEXITCODE)" }

$sizeMB = [math]::Round((Get-Item -LiteralPath $tmpTar).Length / 1MB, 1)
Write-Host "   package size: $sizeMB MB -> $tmpTar"

# ----------------------------------------------------------------- upload
Write-Host "-> uploading package to $remoteTar"
& scp @sshOpts $tmpTar "${target}:$remoteTar"
if ($LASTEXITCODE -ne 0) { throw "Package upload failed (exit $LASTEXITCODE)" }
Remove-Item -LiteralPath $tmpTar -Force

$remoteUnit = "/tmp/fpsgame.service"
if ($InstallService) {
    if (-not (Test-Path -LiteralPath $unitFileLocal)) { throw "Missing $unitFileLocal" }
    Write-Host "-> uploading fpsgame.service"
    & scp @sshOpts $unitFileLocal "${target}:$remoteUnit"
    if ($LASTEXITCODE -ne 0) { throw "Unit upload failed (exit $LASTEXITCODE)" }
}

# ------------------------------------------------------- run remote deploy
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

# --------------------------------------------------------- health check
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
