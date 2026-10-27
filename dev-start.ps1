<#!
.SYNOPSIS
  Starts PhotoSense for local development: the service and the page, on Windows or on a Mac.

.DESCRIPTION
  - Optionally cleans build outputs
  - Builds the service (unless -NoBuild)
  - Starts the service (PhotoSense.Server) and the page's development server (Next.js)
  - Streams what both print, coloured by which one printed it
  - Stops both on Ctrl+C
  - Takes over from a copy that is still running: starting again is how to restart

.PARAMETER NoBuild
  Skip the initial 'dotnet build'.

.PARAMETER Clean
  Run 'dotnet clean' before building.

.PARAMETER ServicePort
  The service's port (default 7071). -FunctionsPort, its old name, still works.

.PARAMETER Open
  After startup, open the default browser to the page.

.PARAMETER Stop
  Stop a copy of PhotoSense that is running, then exit.

.EXAMPLE
  ./dev-start.ps1

.EXAMPLE
  ./dev-start.ps1 -ServicePort 7072

.EXAMPLE
  ./dev-start.ps1 -Stop

.NOTES
  Needs the .NET 10 SDK and Node. Runs in PowerShell 7 (pwsh) on Windows and on a Mac, and in Windows PowerShell.
  On a Mac, PowerShell comes from 'brew install --cask powershell'; start this with 'pwsh ./dev-start.ps1'.
#>
param(
  [switch]$NoBuild,
  [switch]$Clean,
  [Alias('FunctionsPort')][int]$ServicePort = 7071,
  [switch]$Open,
  [switch]$Stop,
  [int]$ReactBasePort = 3000,
  [int]$ReactPortScan = 10
)

$ErrorActionPreference = 'Stop'
# Asked of .NET rather than of $IsWindows, which Windows PowerShell does not have.
$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)

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
    [string]$FileName,
    [string[]]$Arguments,
    [string]$WorkingDirectory,
    [hashtable]$Environment = @{},
    [ConsoleColor]$Color = [ConsoleColor]::Gray
  )
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $FileName
  # One string, quoted where needed, so that this also runs in Windows PowerShell, whose .NET has no ArgumentList.
  $psi.Arguments = ($Arguments | ForEach-Object { if($_ -match '\s'){ '"' + $_ + '"' } else { $_ } }) -join ' '
  $psi.WorkingDirectory = $WorkingDirectory
  foreach($key in $Environment.Keys){ $psi.EnvironmentVariables[$key] = $Environment[$key] }
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
      $text = if($o.Line.Status -eq 'RanToCompletion'){ $o.Line.Result } else { $null }
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

# Every process and the command that started it, on Windows or elsewhere.
function Get-CommandLines {
  if($onWindows){
    Get-CimInstance Win32_Process | ForEach-Object { [pscustomobject]@{ Id = [int]$_.ProcessId; Command = "$($_.Name) $($_.CommandLine)" } }
  } else {
    & ps -axo 'pid=,command=' | ForEach-Object { if($_ -match '^\s*(\d+)\s+(.*)$'){ [pscustomobject]@{ Id = [int]$Matches[1]; Command = $Matches[2] } } }
  }
}

# What a run of this script leaves running: the service and the page's dev server, and what an older run left:
# the Functions host, its worker and the storage emulator. They are recognised by what they are running out of
# this folder, never by port alone, so another program that happens to sit on the same port is left alone.
function Get-PhotoSenseProcesses {
  $here = [regex]::Escape($root)
  Get-CommandLines | Where-Object {
    $cmd = $_.Command
    $_.Id -ne $PID -and $cmd -match $here -and (
      ($cmd -match 'PhotoSense\.Server(\.dll|\.exe)?(\s|"|$)') -or
      ($cmd -match 'PhotoSense\.ReactUI[\\/]+node_modules.*[\\/]next[\\/]') -or
      ($cmd -match 'PhotoSense\.Functions') -or ($cmd -match '(^|[\\/\s])func(\.exe)?\s') -or ($cmd -match 'azurite'))
  }
}

# Ends a process together with everything it started, so that nothing is left holding a port.
function Stop-Tree([int]$processId){
  if($onWindows){ & taskkill.exe /PID $processId /T /F *> $null; return }
  foreach($child in @(& pgrep -P $processId 2>$null)){ if($child){ Stop-Tree ([int]$child) } }
  Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

function Stop-PhotoSense {
  $found = @(Get-PhotoSenseProcesses)
  foreach($p in $found){ Stop-Tree $p.Id }
  if($found.Count -gt 0){ Start-Sleep -Milliseconds 800 }   # the ports take a moment to come free
  return $found.Count
}

function Get-PortOwner([int]$port){
  if($onWindows){
    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if($listener){ return Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue }
    return $null
  }
  $owner = @(& lsof -nP "-iTCP:$port" -sTCP:LISTEN -t 2>$null) | Select-Object -First 1
  if($owner){ return Get-Process -Id ([int]$owner) -ErrorAction SilentlyContinue }
  return $null
}

if($Stop){
  $stopped = Stop-PhotoSense
  if($stopped -gt 0){ Write-Host "Stopped PhotoSense ($stopped processes)." -ForegroundColor Cyan }
  else { Write-Host 'PhotoSense is not running.' -ForegroundColor Cyan }
  return
}

# Starting again takes over from a copy that is still running. Two copies cannot share the ports, and a
# running service keeps its program files locked, which would fail the build below.
$earlier = Stop-PhotoSense
if($earlier -gt 0){ Write-Host "Stopped the copy of PhotoSense that was already running ($earlier processes)." -ForegroundColor DarkYellow }

$busy = Get-PortOwner $ServicePort
if($busy){ throw "Port $ServicePort is in use by $($busy.ProcessName) (process $($busy.Id)), which is not PhotoSense. Close it, or start with -ServicePort <another port>." }

# A build leaves worker processes waiting to be used again, and after building the service they go on
# holding its program files. The next build then cannot replace those files and fails ("Could not copy ...
# The file is locked by: MSBuild"). The ones doing nothing are ended here, whoever left them; one that is
# in the middle of a build, of this project or any other, is using the processor and is left alone.
function Stop-IdleBuildWorkers {
  if(-not $onWindows){ return }   # elsewhere the files are not locked by being open
  $workers = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match '/nodemode:1' } | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
  if($workers.Count -eq 0){ return }
  $before = @{}
  foreach($w in $workers){ $before[$w.Id] = $w.TotalProcessorTime }
  Start-Sleep -Milliseconds 500
  $idle = @($workers | Where-Object { $_.Refresh(); -not $_.HasExited -and $_.TotalProcessorTime -eq $before[$_.Id] })
  foreach($w in $idle){ Stop-Process -Id $w.Id -Force -ErrorAction SilentlyContinue }
  if($idle.Count -gt 0){ Write-Host "Ended $($idle.Count) idle build worker(s) left by an earlier build." -ForegroundColor DarkGray }
}

if($Clean){ Write-Section 'Cleaning solution'; Stop-IdleBuildWorkers; dotnet clean PhotoSense.sln -nodeReuse:false }

$serviceProject = Join-Path $root 'PhotoSense.Server'
if(-not $NoBuild){
  Write-Section 'Building'
  Stop-IdleBuildWorkers
  # The service's project brings the projects it is built from with it. No workers are left waiting
  # afterwards (-nodeReuse:false), so this build does not get in the way of the next one.
  dotnet build (Join-Path $serviceProject 'PhotoSense.Server.csproj') -nodeReuse:false
  if($LASTEXITCODE -ne 0){ throw 'The build failed; PhotoSense was not started.' }
}

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

# The service answers the page's address only, so it is told which port the page was given.
$serviceDll = Join-Path $serviceProject (Join-Path 'bin' (Join-Path 'Debug' (Join-Path 'net10.0' 'PhotoSense.Server.dll')))
$serviceProc = Start-ProcessLogged -Name 'SERVICE' -FileName 'dotnet' -WorkingDirectory $root -Color Yellow -Arguments @(
  $serviceDll, '--urls', "http://localhost:$ServicePort",
  "--Cors:Origins:0=http://localhost:$reactPort", "--Cors:Origins:1=http://127.0.0.1:$reactPort")
Wait-Showing 700
if($serviceProc.HasExited){ Show-Output; throw "The service stopped as it started (code $($serviceProc.ExitCode))." }

# React UI
$reactDir = Join-Path $root 'PhotoSense.ReactUI'
if(!(Test-Path (Join-Path $reactDir 'node_modules'))){
  Write-Section 'Installing React UI dependencies'
  Push-Location $reactDir
  npm install | Out-Null
  Pop-Location
}
$next = Join-Path $reactDir (Join-Path 'node_modules' (Join-Path 'next' (Join-Path 'dist' (Join-Path 'bin' 'next'))))
$reactProc = Start-ProcessLogged -Name 'WEB' -FileName 'node' -WorkingDirectory $reactDir -Color Green -Arguments @($next, 'dev', '-p', "$reactPort") `
  -Environment @{ NEXT_PUBLIC_API_BASE = "http://localhost:$ServicePort/api" }

Write-Section 'Startup summary'
Write-Host "Service:    http://localhost:$ServicePort" -ForegroundColor Yellow
Write-Host ("React UI:   http://localhost:{0}" -f $reactPort) -ForegroundColor Green
Write-Host "Press Ctrl+C here to stop PhotoSense, or run ./dev-start.ps1 -Stop from another terminal." -ForegroundColor Cyan

if($Open){ Start-Process ("http://localhost:{0}" -f $reactPort) | Out-Null }

# Shutdown. Whatever ends the wait below (Ctrl+C, or one of the services dying) lands in the finally
# block, which ends each service this run started together with everything it started in turn.
# It ends nothing else: a later run that has taken over must not lose what it has just started.
$started = @($serviceProc,$reactProc)
function Stop-Started {
  foreach($p in $started){
    if($p -and -not $p.HasExited){ Stop-Tree $p.Id }
  }
}

# Closing the terminal window does not run the finally block, so that case is covered here. The action
# runs apart from this script, so it is handed the processes rather than left to look for them.
Register-EngineEvent PowerShell.Exiting -MessageData $started -Action {
  foreach($p in $Event.MessageData){
    if($p -and -not $p.HasExited){
      if($env:OS -eq 'Windows_NT'){ & taskkill.exe /PID $p.Id /T /F *> $null } else { & pkill -P $p.Id 2>$null; $p.Kill() }
    }
  }
} | Out-Null

try {
  while($true){
    Wait-Showing 1000
    $ended = if($serviceProc.HasExited){ 'Service', $serviceProc } elseif($reactProc.HasExited){ 'React UI', $reactProc }
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
