param(
    [string]$NsisCompiler,
    [string]$WebView2Bootstrapper,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'version.json') -Raw | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version' }
$dist = Join-Path $root 'dist'
$cache = Join-Path $dist 'installer-tools'
$payload = Join-Path $dist ('payload-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $cache, $payload | Out-Null
if (-not $SkipBuild) { & (Join-Path $root 'host\build.ps1') }

if (-not $NsisCompiler) {
    $nsisArchive = Join-Path $cache 'nsis-3.13.zip'
    $NsisCompiler = Join-Path $cache 'nsis-3.13\makensis.exe'
    if (-not (Test-Path -LiteralPath $NsisCompiler)) {
        Invoke-WebRequest 'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.13/nsis-3.13.zip' -OutFile $nsisArchive -UseBasicParsing
        if ((Get-FileHash -LiteralPath $nsisArchive -Algorithm SHA256).Hash -ne 'BA63DFFC4410EE89193E1CB5A41989991BD77C61068DA17E3156D136B7B0B3D8') { throw 'NSIS archive checksum mismatch' }
        Expand-Archive -LiteralPath $nsisArchive -DestinationPath $cache -Force
    }
}
if (-not $WebView2Bootstrapper) {
    $WebView2Bootstrapper = Join-Path $cache 'MicrosoftEdgeWebview2Setup.exe'
    if (-not (Test-Path -LiteralPath $WebView2Bootstrapper)) {
        Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $WebView2Bootstrapper -UseBasicParsing
    }
}
$signature = Get-AuthenticodeSignature -LiteralPath $WebView2Bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation,') { throw 'WebView2 bootstrapper must have a valid Microsoft signature' }
if (-not (Test-Path -LiteralPath $NsisCompiler)) { throw 'NSIS compiler missing' }
$NsisCompiler = [IO.Path]::GetFullPath($NsisCompiler)
$WebView2Bootstrapper = [IO.Path]::GetFullPath($WebView2Bootstrapper)

# A fixed source list prevents other editions or private working files entering this package.
$sources = @('.gitattributes', '.gitignore', 'AGENTS.md', 'LICENSE', 'NOTICE', 'README.md', 'THIRD_PARTY_NOTICES.md', 'TRADEMARKS.md',
    'host\DesktopHost.cs', 'host\Terrarium.ico', 'host\README.md', 'host\app.manifest', 'host\build.ps1', 'host\inspect.ps1',
    'tools\build-installer.ps1', 'tools\installer.nsi', 'tools\version.json')
$themeFiles = @('settings.html', 'thumbs-data.js', 'share.js', 'serve.mjs', 'themes.json', 'i18n\en.json')
$themes = (Get-Content -LiteralPath (Join-Path $root 'prototype\themes.json') -Raw -Encoding UTF8 | ConvertFrom-Json).themes
if ($themes.Count -lt 1) { throw 'themes.json lists no themes' }
# Every theme must be a complete, free, ready theme; its page plus the scripts and images it references are packaged.
foreach ($theme in $themes) {
    if ($theme.status -ne 'ready' -or $theme.product -ne 'free' -or -not $theme.page) { throw 'Theme is not ready, free and complete: ' + $theme.id }
    $page = [string]$theme.page
    if ($page -notmatch '^[a-z0-9-]+\.html$') { throw 'Unexpected theme page: ' + $page }
    if ($themeFiles -notcontains $page) { $themeFiles += $page }
    $html = Get-Content -LiteralPath (Join-Path $root ('prototype\' + $page)) -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($html, 'src="([a-z0-9-]+\.js)"')) {
        if ($themeFiles -notcontains $m.Groups[1].Value) { $themeFiles += $m.Groups[1].Value }
    }
}
# Theme images: everything in prototype\assets belongs to a shipped theme (scripts build some paths at runtime).
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'prototype\assets') -File) {
    if ($file.Extension -notin '.png', '.webp', '.jpg') { throw 'Unexpected asset type: ' + $file.Name }
    $themeFiles += 'assets\' + $file.Name
}
foreach ($theme in $themes) {
    $match = [regex]::Match([string]$theme.thumb, 'url\(thumbs/([^\)?]+)')
    if (-not $match.Success) { throw 'Missing thumbnail URL: ' + $theme.id }
    $thumbName = [uri]::UnescapeDataString($match.Groups[1].Value)
    if ($thumbName -notmatch '^[^\\/]+\.webp$') { throw 'Unexpected thumbnail path: ' + $thumbName }
    $thumbFile = 'thumbs\' + $thumbName
    if ($themeFiles -notcontains $thumbFile) { $themeFiles += $thumbFile }
}
$sources += $themeFiles | ForEach-Object { 'prototype\' + $_ }
$sourceStage = Join-Path $payload 'source-stage'
foreach ($file in $sources) {
    $target = Join-Path $sourceStage $file
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($sourceStage, (Join-Path $payload 'Terrarium-source.zip'))
# Only program data is installed. The accompanying ZIP contains the exact corresponding source.
$runtimeFiles = @('host\bin\Terrarium.exe', 'host\bin\Terrarium.ico', 'host\bin\Microsoft.Web.WebView2.Core.dll', 'host\bin\Microsoft.Web.WebView2.WinForms.dll', 'host\bin\WebView2Loader.dll')
$installFiles = @('LICENSE', 'NOTICE', 'README.md', 'THIRD_PARTY_NOTICES.md', 'TRADEMARKS.md') + @($themeFiles | Where-Object { $_ -ne 'serve.mjs' } | ForEach-Object { 'prototype\' + $_ }) + $runtimeFiles
foreach ($file in $installFiles) {
    $target = Join-Path $payload $file
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
}
$installFiles += 'Terrarium-source.zip'
$utf8 = New-Object Text.UTF8Encoding($true)
$fileLines = @()
$deleteLines = @()
$directories = @{}
foreach ($file in $installFiles) {
    if ($file -match '["$\r\n]') { throw 'Unsafe package path' }
    $dir = Split-Path $file
    $fileLines += 'SetOutPath "$INSTDIR' + $(if ($dir) { '\' + $dir } else { '' }) + '"'
    $fileLines += 'File "' + (Join-Path $payload $file) + '"'
    $deleteLines += 'Delete "$INSTDIR\' + $file + '"'
    while ($dir) { $directories[$dir] = $true; $dir = Split-Path $dir }
}
$deleteLines += $directories.Keys | Sort-Object Length -Descending | ForEach-Object { 'RMDir "$INSTDIR\' + $_ + '"' }
$fileList = Join-Path $payload 'files.nsh'
$deleteList = Join-Path $payload 'delete.nsh'
[IO.File]::WriteAllLines($fileList, [string[]]$fileLines, $utf8)
[IO.File]::WriteAllLines($deleteList, [string[]]$deleteLines, $utf8)
$output = Join-Path $dist "Terrarium-$version-win-x64-Setup.exe"
& $NsisCompiler /V2 "/DVERSION=$version" "/DPAYLOAD=$payload" "/DOUTPUT=$output" "/DBOOTSTRAPPER=$WebView2Bootstrapper" "/DFILE_LIST=$fileList" "/DDELETE_LIST=$deleteList" (Join-Path $PSScriptRoot 'installer.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($output + '.sha256'), "$hash  $([IO.Path]::GetFileName($output))`r`n", (New-Object Text.UTF8Encoding($false)))
$manifest = @($installFiles | ForEach-Object { @{ path = $_.Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath (Join-Path $payload $_) -Algorithm SHA256).Hash } })
[IO.File]::WriteAllText(($output + '.manifest.json'), ($manifest | ConvertTo-Json -Depth 4), $utf8)
Write-Host "Installer: $output"
Write-Host "SHA256: $hash"
