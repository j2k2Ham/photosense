<#!
.SYNOPSIS
  Starts PhotoSense Functions (Azure Functions isolated) and React (Next.js) UI concurrently for local development.

.DESCRIPTION
  - Optionally cleans build outputs
  - Builds the service (unless -NoBuild)
  - Starts the Azurite storage emulator if it is not already running
  - Launches Azure Functions via 'func start' (Core Tools)
  - Launches React UI via 'npm run dev' (Next.js)
  - Streams logs with color differentiation
  - Stops everything it started on Ctrl+C
  - Takes over from a copy that is still running: starting again is how to restart

.PARAMETER NoBuild
  Skip the initial 'dotnet build'.

.PARAMETER Clean
  Run 'dotnet clean' before building.

.PARAMETER FunctionsPort
  Override Functions HTTP port (default 7071).

.PARAMETER Open
  After startup, open the default browser to the React UI URL.

.PARAMETER Stop
  Stop a copy of PhotoSense that is running (service, UI and storage emulator), then exit.

.EXAMPLE
  ./dev-start.ps1

.EXAMPLE
  ./dev-start.ps1 -FunctionsPort 7072

.EXAMPLE
  ./dev-start.ps1 -Stop

.NOTES
  Requires Azure Functions Core Tools v4+ available on PATH (func).
#>
param(
  [switch]$NoBuild,
  [switch]$Clean,
  [int]$FunctionsPort = 7071,
  [switch]$Open,
  [switch]$Stop,
  [switch]$UseDotNetFunctions,
  [int]$FunctionsRetry = 2,
  [int]$ReactBasePort = 3000,
  [int]$ReactPortScan = 10
)

$ErrorActionPreference = 'Stop'

function Write-Section($msg){
  Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

# What the services print is read by this script itself, a line at a time, and never through PowerShell's
# event handlers (Register-ObjectEvent). Those handlers go on firing after the script has handed the
# terminal back, as they do when the services are stopped, and two arriving together at an idle prompt
# race inside PowerShell and end the whole terminal.
$outputs = [System.Collections.Generic.List[hashtable]]::new()

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

  $outputs.Add(@{ Name = $Name; Color = $Color; Reader = $proc.StandardOutput; Line = $proc.StandardOutput.ReadLineAsync() })
  # Tools put notices and progress on their error stream as well as errors, so these lines are not called errors.
  $outputs.Add(@{ Name = $Name; Color = [ConsoleColor]::DarkYellow; Reader = $proc.StandardError; Line = $proc.StandardError.ReadLineAsync() })
  return $proc
}

# Prints the lines the services have written since it was last called.
function Show-Output {
  foreach($o in $outputs){
    # A service that prints without pause is not allowed to hold up the others, or Ctrl+C.
    for($n = 0; $n -lt 500 -and $o.Line -and $o.Line.IsCompleted; $n++){
      $text = if($o.Line.IsCompletedSuccessfully){ $o.Line.Result } else { $null }
      if($null -eq $text){ $o.Line = $null; break }   # the service has closed this stream
      if($text){ Write-Host "[$($o.Name)] $text" -ForegroundColor $o.Color }
      $o.Line = $o.Reader.ReadLineAsync()
    }
  }
}

# Waits, showing what the services print meanwhile.
function Wait-Showing([int]$Milliseconds){
  $until = [DateTime]::UtcNow.AddMilliseconds($Milliseconds)
  do { Start-Sleep -Milliseconds 100; Show-Output } while([DateTime]::UtcNow -lt $until)
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# What a run of this script leaves running: the service and its worker, the UI's dev server and, when it
# started one, the storage emulator. They are recognised by what they are running out of this folder,
# never by port alone, so another program that happens to sit on the same port is left alone.
function Get-PhotoSenseProcesses([switch]$IncludeStorage){
  $here = [regex]::Escape($root)
  Get-CimInstance Win32_Process | Where-Object {
    $cmd = $_.CommandLine
    if(-not $cmd -or $cmd -notmatch $here){ return $false }
    ($_.Name -eq 'func.exe') -or
    ($_.Name -eq 'dotnet.exe' -and $cmd -match 'PhotoSense\.Functions\.dll') -or
    ($_.Name -eq 'node.exe' -and $cmd -match 'PhotoSense\.ReactUI[\\/]+node_modules.*[\\/]next[\\/]') -or
    ($IncludeStorage -and $_.Name -eq 'node.exe' -and $cmd -match 'azurite')
  }
}

# Ends a process together with everything it started. Each service runs underneath a cmd wrapper, and
# ending the wrapper alone leaves the service itself running and holding its port.
function Stop-Tree([int]$processId){
  & taskkill.exe /PID $processId /T /F *> $null
}

function Stop-PhotoSense([switch]$IncludeStorage){
  $found = @(Get-PhotoSenseProcesses -IncludeStorage:$IncludeStorage)
  foreach($p in $found){ Stop-Tree $p.ProcessId }
  if($found.Count -gt 0){ Start-Sleep -Milliseconds 800 }   # the ports take a moment to come free
  return $found.Count
}

function Get-PortOwner([int]$port){
  $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
  if($listener){ Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue }
}

if($Stop){
  $stopped = Stop-PhotoSense -IncludeStorage
  if($stopped -gt 0){ Write-Host "Stopped PhotoSense ($stopped processes)." -ForegroundColor Cyan }
  else { Write-Host 'PhotoSense is not running.' -ForegroundColor Cyan }
  return
}

# Starting again takes over from a copy that is still running. Two copies cannot share the ports, and a
# running service keeps its program files locked, which would fail the build below.
$earlier = Stop-PhotoSense
if($earlier -gt 0){ Write-Host "Stopped the copy of PhotoSense that was already running ($earlier processes)." -ForegroundColor DarkYellow }

$busy = Get-PortOwner $FunctionsPort
if($busy){ throw "Port $FunctionsPort is in use by $($busy.ProcessName) (process $($busy.Id)), which is not PhotoSense. Close it, or start with -FunctionsPort <another port>." }

if($Clean){ Write-Section 'Cleaning solution'; dotnet clean PhotoSense.sln }

if(-not $NoBuild){
  Write-Section 'Building'
  # The service's project brings the projects it is built from with it.
  dotnet build PhotoSense.Functions/PhotoSense.Functions.csproj
  if($LASTEXITCODE -ne 0){ throw 'The build failed; PhotoSense was not started.' }
}

$photoRoot = Join-Path $root 'PhotoSense.Functions'

Write-Section 'Starting services'

function Get-FreePort([int]$start,[int]$count){
  for($p=$start; $p -lt ($start+$count); $p++){
    # Asked of the system rather than tried: a dev server listening on every address does not stop a
    # second program from binding the same port on 127.0.0.1 alone, so trying proves nothing.
    if(-not (Get-PortOwner $p)){ return $p }
  }
  throw "No free port in range $start - $(($start+$count-1))"
}

# Determine React port before launching Next.js to avoid its auto-increment log spam
$reactPort = Get-FreePort -start $ReactBasePort -count $ReactPortScan

# Scans run as a Durable Functions activity, which keeps its state in the storage emulator.
$azuriteProc = $null
$storageUp = $false
try { $probe = [System.Net.Sockets.TcpClient]::new(); $probe.Connect('127.0.0.1', 10000); $probe.Close(); $storageUp = $true } catch { }
if(-not $storageUp){
  if(Get-Command azurite -ErrorAction SilentlyContinue){
    Write-Host 'Starting Azurite storage emulator' -ForegroundColor DarkGray
    $azuriteProc = Start-ProcessLogged -Name 'AZURITE' -Command "azurite --silent --location `"$root\.azurite`"" -WorkingDirectory $root -Color DarkGray
    Wait-Showing 2000
  } else {
    Write-Warning 'Azurite is not running and is not on PATH, so scans will not start. Install it with: npm install -g azurite'
  }
}

# Functions start logic with retry & fallback
function Start-FunctionsHost {
  param([int]$attempt)
  if($UseDotNetFunctions){
    Write-Host "Starting Functions via dotnet run (attempt $attempt)" -ForegroundColor Yellow
    return Start-ProcessLogged -Name 'FUNC' -Command "dotnet run --no-build --project $photoRoot/PhotoSense.Functions.csproj -- --port $FunctionsPort" -WorkingDirectory $photoRoot -Color Yellow
  } else {
    Write-Host "Starting Functions via Core Tools (attempt $attempt)" -ForegroundColor Yellow
    # No --csharp: that flag makes Core Tools use its in-process host, which cannot load this isolated-worker app.
    # The service answers the UI's address only, so it is told which port the UI was given.
    return Start-ProcessLogged -Name 'FUNC' -Command "func start --port $FunctionsPort --cors http://localhost:$reactPort,http://127.0.0.1:$reactPort --script-root $photoRoot/bin/Debug/net8.0" -WorkingDirectory $photoRoot -Color Yellow
  }
}

$funcProc = $null
for($a=1; $a -le ([math]::Max(1,$FunctionsRetry+1)); $a++){
  $funcProc = Start-FunctionsHost -attempt $a
  Wait-Showing 700
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

Write-Section 'Startup summary'
Write-Host "Functions:  http://localhost:$FunctionsPort" -ForegroundColor Yellow
Write-Host ("React UI:   http://localhost:{0}" -f $reactPort) -ForegroundColor Green
Write-Host "Press Ctrl+C here to stop PhotoSense, or run ./dev-start.ps1 -Stop from another terminal." -ForegroundColor Cyan

if($Open){ Start-Process ("http://localhost:{0}" -f $reactPort) | Out-Null }

# Shutdown. Whatever ends the wait below (Ctrl+C, or one of the services dying) lands in the finally
# block, which ends each service this run started together with everything underneath its wrapper.
# It ends nothing else: a later run that has taken over must not lose what it has just started.
$started = @($funcProc,$reactProc,$azuriteProc)
function Stop-Started {
  foreach($p in $started){
    if($p -and -not $p.HasExited){ Stop-Tree $p.Id }
  }
}

# Closing the terminal window does not run the finally block, so that case is covered here. The action
# runs apart from this script, so it is handed the processes rather than left to look for them.
Register-EngineEvent PowerShell.Exiting -MessageData $started -Action {
  foreach($p in $Event.MessageData){
    if($p -and -not $p.HasExited){ & taskkill.exe /PID $p.Id /T /F *> $null }
  }
} | Out-Null

try {
  while($true){
    Wait-Showing 1000
    $ended = if($funcProc.HasExited){ 'Functions host', $funcProc } elseif($reactProc.HasExited){ 'React UI', $reactProc }
    if($ended){
      Wait-Showing 300   # its last lines, which say why
      Write-Host "$($ended[0]) exited (code $($ended[1].ExitCode))" -ForegroundColor Red
      break
    }
  }
}
finally {
  Write-Host "`nStopping PhotoSense..." -ForegroundColor Cyan
  Stop-Started
  Write-Host 'Stopped.' -ForegroundColor Cyan
}
