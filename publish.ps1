<#!
.SYNOPSIS
  Builds PhotoSense as one program for a computer: the service, with the page inside it.

.DESCRIPTION
  Builds the page as plain files, publishes the service for the system named, with .NET included so that
  nothing else need be installed, and puts the page beside it. Start the program and open
  http://localhost:7071 in a browser.

.PARAMETER Runtime
  win-x64 for Windows, osx-arm64 for a Mac with Apple silicon (M1 and later), osx-x64 for an Intel Mac.
  The system this runs on when not given.

.PARAMETER Output
  Where the program goes; publish/<runtime> when not given.

.EXAMPLE
  ./publish.ps1 -Runtime osx-arm64

.NOTES
  Needs the .NET 10 SDK and Node. A Mac build can be made on Windows, and a Windows build on a Mac.
#>
param(
  [ValidateSet('win-x64', 'win-arm64', 'osx-arm64', 'osx-x64')][string]$Runtime,
  [string]$Output
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if(-not $Runtime){
  $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
  $onMac = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)
  $Runtime = if($onMac){ if($arch -eq 'Arm64'){ 'osx-arm64' } else { 'osx-x64' } } elseif($arch -eq 'Arm64'){ 'win-arm64' } else { 'win-x64' }
}
if(-not $Output){ $Output = Join-Path $root (Join-Path 'publish' $Runtime) }

Write-Host "=== Building the page ===" -ForegroundColor Cyan
$reactDir = Join-Path $root 'PhotoSense.ReactUI'
Push-Location $reactDir
try {
  if(!(Test-Path 'node_modules')){ npm ci; if($LASTEXITCODE -ne 0){ throw 'npm ci failed.' } }
  # Served by the service itself, the page asks the address it came from.
  $env:NEXT_PUBLIC_API_BASE = '/api'
  npm run build
  if($LASTEXITCODE -ne 0){ throw 'The page did not build.' }
} finally {
  Remove-Item Env:NEXT_PUBLIC_API_BASE -ErrorAction SilentlyContinue
  Pop-Location
}

Write-Host "=== Publishing the service for $Runtime ===" -ForegroundColor Cyan
dotnet publish (Join-Path $root (Join-Path 'PhotoSense.Server' 'PhotoSense.Server.csproj')) -c Release -r $Runtime --self-contained -o $Output -nodeReuse:false
if($LASTEXITCODE -ne 0){ throw 'The service did not publish.' }

$site = Join-Path $Output 'wwwroot'
if(Test-Path $site){ Remove-Item -LiteralPath $site -Recurse -Force }
Copy-Item -Recurse (Join-Path $reactDir 'out') $site

$program = if($Runtime -like 'win-*'){ 'PhotoSense.Server.exe' } else { './PhotoSense.Server' }
Write-Host "`nPhotoSense is in $Output" -ForegroundColor Green
Write-Host "Start it with $program there, then open http://localhost:7071" -ForegroundColor Green
if($Runtime -like 'osx-*'){
  Write-Host 'On the Mac, the first time: the program is not signed, so macOS asks before it runs. Open it from Finder with Control-click > Open, or run: xattr -dr com.apple.quarantine <the folder>' -ForegroundColor DarkYellow
}
