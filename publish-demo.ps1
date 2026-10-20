$ErrorActionPreference = 'Stop'
$out = "$PSScriptRoot/obj/demo-publish"
$demo = "$PSScriptRoot/demo"

dotnet publish "$PSScriptRoot/src/Murmuration.Browser" -c Release -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Test-Path $demo) { Get-ChildItem $demo -Force | Remove-Item -Recurse -Force }
else { New-Item -ItemType Directory $demo | Out-Null }
Copy-Item "$out/wwwroot/*" $demo -Recurse
