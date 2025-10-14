<#!
.SYNOPSIS
  Starts PhotoSense Functions (Azure Functions isolated) and React (Next.js) UI concurrently for local development.

.DESCRIPTION
  - Ensures required folders exist (photo storage paths from local.settings.json)
  - Optionally cleans build outputs
  - Builds solution (unless -NoBuild)
  - Launches Azure Functions via 'func start' (Core Tools)
  - Launches React UI via 'npm run dev' (Next.js)
  - Optionally can still launch Blazor Server with -IncludeBlazor
  - Streams logs with color differentiation
  - Supports graceful shutdown on Ctrl+C

.PARAMETER NoBuild
  Skip the initial 'dotnet build'.

.PARAMETER Clean
  Run 'dotnet clean' before building.

.PARAMETER FunctionsPort
  Override Functions HTTP port (default 7071).

.PARAMETER UseWatch
  Use 'dotnet watch run' for Blazor hot reload instead of plain 'dotnet run' (only if -IncludeBlazor).

.PARAMETER IncludeBlazor
  Also start the legacy Blazor Server UI.

.PARAMETER Open
  After startup, open the default browser to the React UI URL.

.EXAMPLE
  ./dev-start.ps1

.EXAMPLE
  ./dev-start.ps1 -UseWatch -FunctionsPort 7072

.NOTES
  Requires Azure Functions Core Tools v4+ available on PATH (func).
#>
param(
  [switch]$NoBuild,
  [switch]$Clean,
  [int]$FunctionsPort = 7071,
  [switch]$UseWatch,
  [switch]$Open,
  [switch]$IncludeBlazor,
  [switch]$ForceUnlock,
  [switch]$UseDotNetFunctions,
  [int]$FunctionsRetry = 2,
  [int]$ReactBasePort = 3000,
  [int]$ReactPortScan = 10,
  [switch]$Diagnostics,
  [int]$HealthRetry = 5,
  [int]$HealthIntervalMs = 800
)

$ErrorActionPreference = 'Stop'

function Write-Section($msg){
  Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

function Start-ProcessLogged {
  param(
    [string]$Name,
    [string]$Command,
    [string]$WorkingDirectory,
    [ConsoleColor]$Color = [ConsoleColor]::Gray
  )
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $env:ComSpec
  $psi.Arguments = "/c $Command"
  $psi.WorkingDirectory = $WorkingDirectory
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $proc = New-Object System.Diagnostics.Process
  $proc.StartInfo = $psi

  $null = $proc.Start()

  Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived -Action { if ($EventArgs.Data) { Write-Host "[$Name] $($EventArgs.Data)" -ForegroundColor $Color } } | Out-Null
  Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived -Action { if ($EventArgs.Data) { Write-Host "[$Name][ERR] $($EventArgs.Data)" -ForegroundColor Red } } | Out-Null

  $proc.BeginOutputReadLine(); $proc.BeginErrorReadLine()
  return $proc
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if($Clean){ Write-Section 'Cleaning solution'; dotnet clean PhotoSense.sln }

# Enhancement B: Optionally kill stale Blazor processes holding file locks
if($ForceUnlock){
  Write-Section 'Force unlocking stale processes (Blazor/React)'
  # Kill Blazor hosts
  Get-Process | Where-Object { $_.ProcessName -like 'PhotoSense.BlazorServer*' -or ($_.ProcessName -eq 'dotnet' -and $_.MainWindowTitle -like '*Blazor*') } | ForEach-Object {
    try { Write-Host "Killing Blazor PID $($_.Id) ($($_.ProcessName))" -ForegroundColor DarkYellow; Stop-Process -Id $_.Id -Force } catch { Write-Warning "Failed to kill PID $($_.Id): $($_.Exception.Message)" }
  }
  # Kill Node processes occupying common React ports
  function Get-PortPids($port){
    try {
      if(Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue){
        Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -ErrorAction SilentlyContinue
      } else {
        netstat -ano | Select-String -Pattern ":$port\s" | ForEach-Object { ($_ -split '\s+')[-1] } | Where-Object { $_ -match '^\d+$' } | Select-Object -Unique
      }
    } catch { @() }
  }
  for($p=$ReactBasePort; $p -lt ($ReactBasePort + $ReactPortScan); $p++){
    $pids = Get-PortPids $p
    foreach($portPid in $pids){
      try {
        $proc = Get-Process -Id $portPid -ErrorAction SilentlyContinue
        if($proc -and ($proc.ProcessName -like 'node*' -or $proc.ProcessName -eq 'cmd' -or $proc.ProcessName -eq 'pwsh')){
          Write-Host "Killing process $portPid holding port $p ($($proc.ProcessName))" -ForegroundColor DarkYellow
          Stop-Process -Id $portPid -Force
        }
      } catch { }
    }
  }
}

# Enhancement C: Build subset unless Blazor explicitly requested
if(-not $NoBuild){
  if($IncludeBlazor){
    Write-Section 'Building full solution (including Blazor)'
    dotnet build PhotoSense.sln
  } else {
    Write-Section 'Building core projects (excluding Blazor)'
    $projects = @(
      'PhotoSense.Domain/PhotoSense.Domain.csproj',
      'PhotoSense.Contracts/PhotoSense.Contracts.csproj',
      'PhotoSense.Application/PhotoSense.Application.csproj',
      'PhotoSense.Infrastructure/PhotoSense.Infrastructure.csproj',
      'PhotoSense.Functions/PhotoSense.Functions.csproj',
      'PhotoSense.ReactUI/PhotoSense.ReactUI.csproj'  # placeholder if we add a backend build for shared TS generation later
    ) | Where-Object { Test-Path $_ }
    foreach($p in $projects){
      Write-Host "-> building $p" -ForegroundColor Gray
      dotnet build $p
    }
  }
}

# Ensure photo storage directories exist (paths from local.settings.json PhotoStorage section)
$photoRoot = Join-Path $root 'PhotoSense.Functions'
$primary = Join-Path $photoRoot 'photos/primary'
$secondary = Join-Path $photoRoot 'photos/secondary'
$null = New-Item -ItemType Directory -Force -Path $primary | Out-Null
$null = New-Item -ItemType Directory -Force -Path $secondary | Out-Null

Write-Section 'Starting services'

function Get-FreePort([int]$start,[int]$count){
  for($p=$start; $p -lt ($start+$count); $p++){
    $listener = $null
    try {
      $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,$p)
      $listener.Start(); $listener.Stop(); return $p
    } catch { if($listener){ try { $listener.Stop() } catch {} } }
  }
  throw "No free port in range $start - $(($start+$count-1))"
}

# Determine React port before launching Next.js to avoid its auto-increment log spam
$reactPort = Get-FreePort -start $ReactBasePort -count $ReactPortScan

# Functions start logic with retry & fallback
function Start-FunctionsHost {
  param([int]$attempt)
  if($UseDotNetFunctions){
    Write-Host "Starting Functions via dotnet run (attempt $attempt)" -ForegroundColor Yellow
    return Start-ProcessLogged -Name 'FUNC' -Command "dotnet run --no-build --project $photoRoot/PhotoSense.Functions.csproj -- --port $FunctionsPort" -WorkingDirectory $photoRoot -Color Yellow
  } else {
    Write-Host "Starting Functions via Core Tools (attempt $attempt)" -ForegroundColor Yellow
    return Start-ProcessLogged -Name 'FUNC' -Command "func start --csharp --port $FunctionsPort --script-root $photoRoot/bin/Debug/net8.0" -WorkingDirectory $photoRoot -Color Yellow
  }
}

$funcProc = $null
for($a=1; $a -le ([math]::Max(1,$FunctionsRetry+1)); $a++){
  $funcProc = Start-FunctionsHost -attempt $a
  Start-Sleep -Milliseconds 700
  if(-not $funcProc.HasExited){ break }
  Write-Warning "Functions host exited immediately (attempt $a)."
  if(-not $UseDotNetFunctions -and $a -eq 1){
    Write-Host 'Falling back to dotnet run for Functions...' -ForegroundColor DarkYellow
    $UseDotNetFunctions = $true
  }
}
if($funcProc -and $funcProc.HasExited){ throw "Failed to start Functions host after retries." }

# React UI
$reactDir = Join-Path $root 'PhotoSense.ReactUI'
if(!(Test-Path (Join-Path $reactDir 'node_modules'))){
  Write-Section 'Installing React UI dependencies'
  Push-Location $reactDir
  npm install | Out-Null
  Pop-Location
}
$env:PORT = $reactPort
$reactProc = Start-ProcessLogged -Name 'WEB' -Command 'npm run dev' -WorkingDirectory $reactDir -Color Green

# Optional Blazor
$blazorProc = $null
$httpsPort = $null; $httpPort = $null
if($IncludeBlazor){
  $blazorDir = Join-Path $root 'PhotoSense.BlazorServer'
  $launchSettings = Join-Path $blazorDir 'Properties/launchSettings.json'
  if(Test-Path $launchSettings){
    try {
      $json = Get-Content $launchSettings -Raw | ConvertFrom-Json
      $profiles = $json.profiles | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name
      foreach($p in $profiles){
        $appUrl = $json.profiles.$p.applicationUrl
        if($appUrl){
          foreach($part in $appUrl -split ';'){
            if($part -match '^https://localhost:(\d+)$'){ $httpsPort = $Matches[1] }
            if($part -match '^http://localhost:(\d+)$'){ $httpPort = $Matches[1] }
          }
          if($httpsPort -or $httpPort){ break }
        }
      }
    } catch { }
  }
  $blazorCmd = $UseWatch ? 'dotnet watch run' : 'dotnet run'
  $blazorProc = Start-ProcessLogged -Name 'BLAZOR' -Command $blazorCmd -WorkingDirectory $blazorDir -Color Magenta
}

Write-Section 'Startup summary'
Write-Host "Functions:  http://localhost:$FunctionsPort" -ForegroundColor Yellow
Write-Host ("React UI:   http://localhost:{0}" -f $reactPort) -ForegroundColor Green
if($IncludeBlazor){
  if($httpsPort -or $httpPort){
    Write-Host ("Blazor:     https://localhost:{0}  (http://localhost:{1})" -f $httpsPort,$httpPort) -ForegroundColor Magenta
  } else {
    Write-Host 'Blazor:     (See launchSettings for exact ports, printed in BLAZOR logs)' -ForegroundColor Magenta
  }
}

Write-Host "Press Ctrl+C to stop both." -ForegroundColor Cyan

if($Open){ Start-Process ("http://localhost:{0}" -f $reactPort) | Out-Null }

# Emit JSON startup summary
$summary = [pscustomobject]@{
  timestamp = (Get-Date).ToString('o')
  functionsUrl = "http://localhost:$FunctionsPort"
  reactUrl = "http://localhost:$reactPort"
  includeBlazor = [bool]$IncludeBlazor
  usedDotNetFunctions = [bool]$UseDotNetFunctions
  pid = $PID
  diagnostics = [bool]$Diagnostics
  health = @{}
}
$summaryPath = Join-Path $root '.dev-start-summary.json'
$summary | ConvertTo-Json -Depth 4 | Out-File -FilePath $summaryPath -Encoding utf8
Write-Host "Wrote summary: $summaryPath" -ForegroundColor Cyan

# Health checks (simple pings)
function Test-Url($url){
  try { (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 5).StatusCode } catch { 0 }
}

function Update-SummaryHealth($key,$status){
  try {
    $json = Get-Content $summaryPath -Raw | ConvertFrom-Json
    $json.health[$key] = $status
    $json | ConvertTo-Json -Depth 6 | Out-File $summaryPath -Encoding utf8
  } catch {}
}

Start-Job -ScriptBlock {
  param($funcUrl,$reactUrl,$summaryPath,$retries,$intervalMs,$diag)
  function Ping($u){ try { (Invoke-WebRequest -UseBasicParsing -Uri $u -TimeoutSec 5).StatusCode } catch { 0 } }
  $funcStatus = 0; $reactStatus = 0
  for($i=0; $i -lt $retries; $i++){
    Start-Sleep -Milliseconds $intervalMs
    if($funcStatus -eq 0){
      $funcStatus = Ping "$funcUrl/api/scan/logs"
      if($funcStatus -eq 0){ $funcStatus = Ping $funcUrl }
      if($funcStatus -ne 0){
        if($funcStatus -ge 200 -and $funcStatus -lt 400){ Write-Host "[HEALTH] Functions OK ($funcStatus)" -ForegroundColor Green } else { Write-Host "[HEALTH] Functions FAIL ($funcStatus)" -ForegroundColor Red }
        if($diag){ Write-Host "[DIAG] Functions attempt $i status $funcStatus" -ForegroundColor DarkCyan }
        try { $js = Get-Content $summaryPath -Raw | ConvertFrom-Json; $js.health.functions = $funcStatus; $js | ConvertTo-Json -Depth 6 | Out-File $summaryPath -Encoding utf8 } catch {}
      }
    }
    if($reactStatus -eq 0){
      $reactStatus = Ping $reactUrl
      if($reactStatus -ne 0){
        if($reactStatus -ge 200 -and $reactStatus -lt 400){ Write-Host "[HEALTH] React OK ($reactStatus)" -ForegroundColor Green } else { Write-Host "[HEALTH] React FAIL ($reactStatus)" -ForegroundColor Red }
        if($diag){ Write-Host "[DIAG] React attempt $i status $reactStatus" -ForegroundColor DarkCyan }
        try { $js = Get-Content $summaryPath -Raw | ConvertFrom-Json; $js.health.react = $reactStatus; $js | ConvertTo-Json -Depth 6 | Out-File $summaryPath -Encoding utf8 } catch {}
      }
    }
    if($funcStatus -ne 0 -and $reactStatus -ne 0){ break }
  }
  if($funcStatus -eq 0){ Write-Host '[HEALTH] Functions UNREACHABLE' -ForegroundColor Red }
  if($reactStatus -eq 0){ Write-Host '[HEALTH] React UNREACHABLE' -ForegroundColor Red }
} -ArgumentList "http://localhost:$FunctionsPort","http://localhost:$reactPort",$summaryPath,$HealthRetry,$HealthIntervalMs,$Diagnostics | Out-Null

if($Diagnostics){ Write-Host "Diagnostics enabled: retries=$HealthRetry interval=${HealthIntervalMs}ms" -ForegroundColor DarkCyan }

# Graceful shutdown
$stopping = $false

$handler = {
  if($stopping){ return }
  $script:stopping = $true
  Write-Host "`nStopping processes..." -ForegroundColor Cyan
  foreach($p in @($funcProc,$reactProc,$blazorProc)){
    if($p -and -not $p.HasExited){
      try { $p.Kill() } catch { }
    }
  }
  Write-Host 'Done.' -ForegroundColor Cyan
  exit
}

# Trap Ctrl+C
Register-EngineEvent PowerShell.Exiting -Action $handler | Out-Null

while(-not $stopping){
  Start-Sleep -Seconds 1
  if($funcProc.HasExited){ Write-Host "Functions host exited (code $($funcProc.ExitCode))" -ForegroundColor Red; break }
  if($reactProc.HasExited){ Write-Host "React UI exited (code $($reactProc.ExitCode))" -ForegroundColor Red; break }
  if($blazorProc -and $blazorProc.HasExited){ Write-Host "Blazor host exited (code $($blazorProc.ExitCode))" -ForegroundColor Red; break }
}

# Cleanup if one exited
& $handler
