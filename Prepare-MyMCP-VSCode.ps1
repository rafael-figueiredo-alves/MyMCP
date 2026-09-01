[CmdletBinding()]
param(
  [switch]$Install,
  [switch]$OpenWorkspace,
  [switch]$KeepStaging,
  [switch]$RebuildServer,
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
  Write-Host "==> $Message"
}

$root = $PSScriptRoot
$extensionRoot = Join-Path $root 'vscode-extension'
$serverProject = Join-Path $root 'server\MyMcp.Server\MyMcp.Server.csproj'
$serverOutput = Join-Path $root "server\MyMcp.Server\bin\$Configuration\net10.0"

if (-not (Test-Path $extensionRoot)) {
  throw "Extension folder not found: $extensionRoot"
}

if (-not (Test-Path (Join-Path $extensionRoot 'package.json'))) {
  throw "Extension manifest not found: $extensionRoot\package.json"
}

if (-not (Test-Path $serverProject)) {
  throw "Server project not found: $serverProject"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw '.NET SDK not found on PATH.'
}

if (-not (Get-Command vsce -ErrorAction SilentlyContinue)) {
  throw 'vsce not found on PATH. Install it before running this script.'
}

if ($Install -and -not (Get-Command code -ErrorAction SilentlyContinue)) {
  throw 'code not found on PATH. Install VS Code CLI or run without -Install.'
}

if ($RebuildServer -or -not (Test-Path (Join-Path $serverOutput 'MyMcp.Server.exe'))) {
  Write-Step "Building MyMCP server ($Configuration)"
  & dotnet build $serverProject -c $Configuration --no-restore
  if ($LASTEXITCODE -ne 0) {
    throw 'dotnet build failed.'
  }
}
else {
  Write-Step "Using existing server build output"
}

if (-not (Test-Path (Join-Path $serverOutput 'MyMcp.Server.exe'))) {
  throw "Server output not found: $serverOutput"
}

$artifactsRoot = Join-Path $root 'artifacts'
$vsixName = 'mymcp-vscode-extension.vsix'
$vsixPath = Join-Path $artifactsRoot $vsixName
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mymcp-vscode-stage-" + [Guid]::NewGuid().ToString('N'))
$stageExtension = Join-Path $stageRoot 'extension'
$stageServer = Join-Path $stageExtension "server\MyMcp.Server\bin\$Configuration\net10.0"

Write-Step "Creating staging directory"
New-Item -ItemType Directory -Path $stageExtension -Force | Out-Null
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

try {
  Write-Step "Copying extension files"
  Get-ChildItem -Force $extensionRoot |
    Where-Object { $_.Name -ne 'node_modules' -and $_.Extension -ne '.vsix' } |
    Copy-Item -Destination $stageExtension -Recurse -Force

  $licensePath = Join-Path $root 'LICENSE'
  if (Test-Path $licensePath) {
    Copy-Item $licensePath -Destination (Join-Path $stageExtension 'LICENSE') -Force
  }

  Write-Step "Copying server output into extension payload"
  New-Item -ItemType Directory -Path $stageServer -Force | Out-Null
  Get-ChildItem -Force $serverOutput | Copy-Item -Destination $stageServer -Recurse -Force

  if (Test-Path $vsixPath) {
    Remove-Item $vsixPath -Force
  }

  Write-Step "Packaging VS Code extension"
  Push-Location $stageExtension
  try {
    & vsce package --allow-missing-repository --out $vsixPath
    if ($LASTEXITCODE -ne 0) {
      throw 'vsce package failed.'
    }
  }
  finally {
    Pop-Location
  }

  Write-Host "VSIX created at: $vsixPath"

  if ($Install) {
    Write-Step "Installing extension into VS Code"
    & code --install-extension $vsixPath --force
    if ($LASTEXITCODE -ne 0) {
      throw 'VS Code extension installation failed.'
    }
  }

  if ($OpenWorkspace) {
    Write-Step "Opening workspace in VS Code"
    & code $root
  }
}
finally {
  if (-not $KeepStaging -and (Test-Path $stageRoot)) {
    Remove-Item $stageRoot -Recurse -Force
  }
}
