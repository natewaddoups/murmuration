$ErrorActionPreference = 'Stop'
$out = "$PSScriptRoot/obj/demo-publish"
$demo = "$PSScriptRoot/demo"

Remove-ChildItem $out -Recurse -Force -ErrorAction SilentlyContinue
Remove-ChildItem $demo -Recurse -Force -ErrorAction SilentlyContinue
mkdir $demo

dotnet publish "$PSScriptRoot/src/Murmuration.Browser" -c Release -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Test-Path $demo) { Get-ChildItem $demo -Force | Remove-Item -Recurse -Force }
else { New-Item -ItemType Directory $demo | Out-Null }
Copy-Item "$out/wwwroot/*" $demo -Recurse
New-Item -ItemType File "$demo/.nojekyll" | Out-Null
