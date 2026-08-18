# Builds Release and zips only NINA.Plugin.SeeDark.dll (NINA loads plugins in-process;
# do not use dotnet publish here — it bundles a duplicate of NINA's dependency graph).
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$csproj = Join-Path $repoRoot 'NINA.Plugin.SeeDark\NINA.Plugin.SeeDark.csproj'
$assemblyInfo = Join-Path $repoRoot 'NINA.Plugin.SeeDark\Properties\AssemblyInfo.cs'

if (-not (Test-Path $csproj)) { throw "Cannot find csproj: $csproj" }

Push-Location $repoRoot
try {
    dotnet build $csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

$dll = Join-Path $repoRoot 'NINA.Plugin.SeeDark\bin\Release\net8.0-windows\NINA.Plugin.SeeDark.dll'
if (-not (Test-Path $dll)) { throw "Missing output: $dll" }

$versionLine = Select-String -Path $assemblyInfo -Pattern 'AssemblyVersion\("([^"]+)"\)' | Select-Object -First 1
if (-not $versionLine) { throw "Could not read AssemblyVersion from $assemblyInfo" }
$version = $versionLine.Matches[0].Groups[1].Value

$outDir = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$zipName = "NINA.Plugin.SeeDark-$version.zip"
$zipPath = Join-Path $outDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Compress-Archive -LiteralPath $dll -DestinationPath $zipPath -Force

# SHA256 sidecar for NINA plugin manager integrity verification.
$shaPath = "$zipPath.sha256"
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $zipName" | Set-Content -Path $shaPath -Encoding ascii

Write-Host "Created $zipPath"
Write-Host "Created $shaPath"
