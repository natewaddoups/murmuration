param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/screensaver/win-x64'))

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src/Murmuration.Desktop/Murmuration.Desktop.csproj'

dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $outputDirectory `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$appHost = Join-Path $outputDirectory 'Murmuration.exe'
if (-not (Test-Path -LiteralPath $appHost)) {
    throw "Expected apphost was not produced: $appHost"
}

Copy-Item -LiteralPath $appHost -Destination (Join-Path $outputDirectory 'Murmuration.scr') -Force
Remove-Item -LiteralPath $appHost
Get-ChildItem -LiteralPath $outputDirectory -Filter '*.pdb' | Remove-Item -Force
Write-Output "Screen saver package: $outputDirectory"