[CmdletBinding()]
param (
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^\d+\.\d+\.\d+$')]
  [string] $Version,

  [string] $OutputDirectory,

  [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath(
  (Join-Path $PSScriptRoot '..')
)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
  $OutputDirectory = Join-Path $repositoryRoot "artifacts\v$Version"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $OutputDirectory) {
  throw "Output directory already exists: $OutputDirectory"
}

Push-Location $repositoryRoot

try {
  if (-not $SkipBuild) {
    & dotnet build 'websocket-sharp\websocket-sharp.csproj' -c Release --no-restore

    if ($LASTEXITCODE -ne 0) {
      throw "Release build failed with exit code $LASTEXITCODE."
    }
  }

  $dllPath = Join-Path $repositoryRoot 'websocket-sharp\bin\Release\net472\websocket-sharp.dll'

  if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw "Release DLL was not found: $dllPath"
  }

  $expectedFileVersion = "$Version.0"
  $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath)

  if ($versionInfo.FileVersion -ne $expectedFileVersion) {
    throw "DLL file version '$($versionInfo.FileVersion)' does not match '$expectedFileVersion'."
  }

  if ($versionInfo.ProductVersion -ne $expectedFileVersion) {
    throw "DLL product version '$($versionInfo.ProductVersion)' does not match '$expectedFileVersion'."
  }

  $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath)

  if ($assemblyName.Name -ne 'websocket-sharp') {
    throw "Unexpected assembly name: $($assemblyName.Name)"
  }

  if ($assemblyName.Version.ToString() -ne '1.0.2.32832') {
    throw "Unexpected AssemblyVersion: $($assemblyName.Version)"
  }

  $publicKeyToken = ($assemblyName.GetPublicKeyToken() | ForEach-Object {
    $_.ToString('x2')
  }) -join ''

  if ($publicKeyToken -ne '5660b08a1845a91e') {
    throw "Unexpected public key token: $publicKeyToken"
  }

  $notesPath = Join-Path $repositoryRoot 'RELEASE_NOTES.md'
  $expectedHeading = "# websocket-sharp v$Version"

  if (-not (Select-String -LiteralPath $notesPath -SimpleMatch $expectedHeading -Quiet)) {
    throw "RELEASE_NOTES.md does not contain '$expectedHeading'."
  }

  New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

  $standaloneDll = Join-Path $OutputDirectory 'websocket-sharp.dll'
  Copy-Item -LiteralPath $dllPath -Destination $standaloneDll

  $packageName = "websocket-sharp-v$Version-unity-net472"
  $packageDirectory = Join-Path $OutputDirectory $packageName
  New-Item -ItemType Directory -Path $packageDirectory | Out-Null

  Copy-Item -LiteralPath $dllPath -Destination (Join-Path $packageDirectory 'websocket-sharp.dll')
  Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE.txt') -Destination $packageDirectory
  Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $packageDirectory
  Copy-Item -LiteralPath $notesPath -Destination $packageDirectory

  $zipPath = Join-Path $OutputDirectory "$packageName.zip"
  Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $zipPath

  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)

  try {
    $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    $expectedEntries = @(
      'LICENSE.txt',
      'README.md',
      'RELEASE_NOTES.md',
      'websocket-sharp.dll'
    ) | Sort-Object

    if (($actualEntries -join '|') -ne ($expectedEntries -join '|')) {
      throw "Unexpected ZIP contents: $($actualEntries -join ', ')"
    }
  }
  finally {
    $archive.Dispose()
  }

  $checksumPath = Join-Path $OutputDirectory 'SHA256SUMS.txt'
  $checksumLines = @($standaloneDll, $zipPath) | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($_))"
  }

  Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ASCII

  Write-Host "Release assets created in $OutputDirectory"
  Get-ChildItem -LiteralPath $OutputDirectory -File |
    Select-Object Name, Length |
    Format-Table -AutoSize
}
finally {
  Pop-Location
}
