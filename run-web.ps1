param(
	[int]$Port = 5087,
	[switch]$NoOpen
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$publishDir = Join-Path $root 'obj/demo-publish'
$siteDir = Join-Path $root 'demo'

Write-Host "Publishing browser build..."
dotnet publish (Join-Path $root 'src/Murmuration.Browser') -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Staging static site..."
if (Test-Path $siteDir) {
	Get-ChildItem $siteDir -Force | Remove-Item -Recurse -Force
} else {
	New-Item -ItemType Directory -Path $siteDir | Out-Null
}
Copy-Item (Join-Path $publishDir 'wwwroot/*') $siteDir -Recurse -Force

# GitHub Pages needs this; harmless for local preview.
New-Item -ItemType File -Path (Join-Path $siteDir '.nojekyll') -Force | Out-Null

$url = "http://127.0.0.1:$Port/"
if (-not $NoOpen) {
	Write-Host "Opening $url"
	Start-Process $url
}

Write-Host "Serving $siteDir at $url"
Write-Host "Press Ctrl+C to stop."

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($url)

try {
	$listener.Start()

	$contentTypes = @{
		'.html' = 'text/html; charset=utf-8'
		'.js' = 'text/javascript; charset=utf-8'
		'.mjs' = 'text/javascript; charset=utf-8'
		'.css' = 'text/css; charset=utf-8'
		'.json' = 'application/json; charset=utf-8'
		'.wasm' = 'application/wasm'
		'.map' = 'application/json; charset=utf-8'
		'.dat' = 'application/octet-stream'
		'.pdb' = 'application/octet-stream'
		'.dll' = 'application/octet-stream'
		'.gz' = 'application/gzip'
		'.png' = 'image/png'
		'.jpg' = 'image/jpeg'
		'.jpeg' = 'image/jpeg'
		'.svg' = 'image/svg+xml'
		'.ico' = 'image/x-icon'
	}

	while ($listener.IsListening) {
		$context = $listener.GetContext()
		try {
			$relativePath = $context.Request.Url.AbsolutePath.TrimStart('/')
			if ([string]::IsNullOrWhiteSpace($relativePath)) {
				$relativePath = 'index.html'
			}

			$relativePath = $relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar
			$requestedFile = Join-Path $siteDir $relativePath
			$fullSitePath = [System.IO.Path]::GetFullPath($siteDir)
			$fullRequestPath = [System.IO.Path]::GetFullPath($requestedFile)

			# Block path traversal; only serve files under $siteDir.
			if (-not $fullRequestPath.StartsWith($fullSitePath, [System.StringComparison]::OrdinalIgnoreCase)) {
				$context.Response.StatusCode = 403
				$context.Response.Close()
				continue
			}

			if (-not (Test-Path $fullRequestPath -PathType Leaf)) {
				$context.Response.StatusCode = 404
				$context.Response.Close()
				continue
			}

			$ext = [System.IO.Path]::GetExtension($fullRequestPath).ToLowerInvariant()
			$contentType = $contentTypes[$ext]
			if (-not $contentType) {
				$contentType = 'application/octet-stream'
			}

			$bytes = [System.IO.File]::ReadAllBytes($fullRequestPath)
			$context.Response.StatusCode = 200
			$context.Response.ContentType = $contentType
			$context.Response.ContentLength64 = $bytes.LongLength
			$context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
			$context.Response.Close()
		}
		catch {
			try {
				$context.Response.StatusCode = 500
				$context.Response.Close()
			}
			catch {
				# Ignore close errors for aborted requests.
			}
		}
	}
}
finally {
	if ($listener.IsListening) {
		$listener.Stop()
	}
	$listener.Close()
}