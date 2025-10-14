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
  [switch]$IncludeBlazor
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
if(-not $NoBuild){ Write-Section 'Building solution'; dotnet build PhotoSense.sln | Out-Null }

# Ensure photo storage directories exist (paths from local.settings.json PhotoStorage section)
$photoRoot = Join-Path $root 'PhotoSense.Functions'
$primary = Join-Path $photoRoot 'photos/primary'
$secondary = Join-Path $photoRoot 'photos/secondary'
$null = New-Item -ItemType Directory -Force -Path $primary | Out-Null
$null = New-Item -ItemType Directory -Force -Path $secondary | Out-Null

Write-Section 'Starting services'

# Functions
$funcCmd = "func start --csharp --port $FunctionsPort --script-root $photoRoot/bin/Debug/net8.0"
$funcProc = Start-ProcessLogged -Name 'FUNC' -Command $funcCmd -WorkingDirectory $photoRoot -Color Yellow

# React UI
$reactDir = Join-Path $root 'PhotoSense.ReactUI'
if(!(Test-Path (Join-Path $reactDir 'node_modules'))){
  Write-Section 'Installing React UI dependencies'
  Push-Location $reactDir
  npm install | Out-Null
  Pop-Location
}
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
Write-Host 'React UI:   http://localhost:3000' -ForegroundColor Green
if($IncludeBlazor){
  if($httpsPort -or $httpPort){
    Write-Host ("Blazor:     https://localhost:{0}  (http://localhost:{1})" -f $httpsPort,$httpPort) -ForegroundColor Magenta
  } else {
    Write-Host 'Blazor:     (See launchSettings for exact ports, printed in BLAZOR logs)' -ForegroundColor Magenta
  }
}

Write-Host "Press Ctrl+C to stop both." -ForegroundColor Cyan

if($Open){ Start-Process 'http://localhost:3000' | Out-Null }

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
