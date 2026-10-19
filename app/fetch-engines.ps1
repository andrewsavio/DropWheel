# Downloads the open-source engines DropWheel bundles into app\DropWheel\engines (git-ignored, copied next to the exe).
# Pinned versions from each project's official release page; every download is checked against its SHA-256.
#   pwsh app/fetch-engines.ps1            fetch what is missing
#   pwsh app/fetch-engines.ps1 -Force     fetch everything again
param([switch]$Force)
$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot "DropWheel\engines"
$cache = Join-Path $env:TEMP "dropwheel-engine-downloads"
New-Item -ItemType Directory -Force $root, $cache | Out-Null

# name, url, sha256 (empty = print it on first download so it can be pinned), files to keep (relative to the archive root; * = all)
$engines = @(
  @{ Name = "imagemagick"; Url = "https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-34/ImageMagick-7.1.2-34-portable-Q16-HDRI-x64.7z"; Sha = "8dcfd3644b7fc20a92aeedeb4798024e5947f25f4d7c0c313c883fbca3d723e6"; Keep = "*"; Drop = "compare.exe", "composite.exe", "conjure.exe", "identify.exe", "mogrify.exe", "montage.exe", "stream.exe", "ChangeLog.md" }
  @{ Name = "oxipng";      Url = "https://github.com/oxipng/oxipng/releases/download/v10.2.1/oxipng-10.2.1-x86_64-pc-windows-msvc.zip"; Sha = "7e940f83ee46874b73f53031f96a15834cb70b220af27391fb06fe7b4dd798e1"; Keep = "*" }
  @{ Name = "libjpeg-turbo"; Url = "https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/3.2.0/libjpeg-turbo-3.2.0-vc-x64.exe"; Sha = "662761d8ba8dae04aec74023ebaeceb856c2b56b9b59cfd180759d26300dda42"; Keep = "bin\jpegtran.exe", "bin\jpeg62.dll", "doc\LICENSE.md", "doc\README.ijg" }
  @{ Name = "qpdf";        Url = "https://github.com/qpdf/qpdf/releases/download/v12.4.2/qpdf-12.4.2-msvc64.zip"; Sha = "db87077e683630c1217e0e8f9a20a9749d952ab676e881c3689187763a5de25d"; Keep = "bin\*", "share\doc\qpdf\manual-html\license.html" }
  @{ Name = "ghostscript"; Url = "https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/download/gs10080/gs10080w64.exe"; Sha = "52a91b8bf09298788d7a57b9206127026c23eacd75405f0a131e26dc381dce50"; Keep = "bin\*", "lib", "doc\COPYING" }
  @{ Name = "libreoffice"; Url = "https://download.documentfoundation.org/libreoffice/stable/26.8.1/win/x86_64/LibreOffice_26.8.1_Win_x86-64.msi"; Sha = "c6298b10cfa1748bcbedad94c62ce771e240e5021bfd6d7a967cd571b04b5312"; Keep = "*" }
  @{ Name = "gifsicle";    Url = "https://eternallybored.org/misc/gifsicle/releases/gifsicle-1.97-win64.zip"; Sha = "bb3ad4e1521a3953152f406eaafabda153fb5986938405860a7e7f12a3d13227"; Keep = "*" }
)

$sevenZip = (Get-Command 7z -ErrorAction SilentlyContinue).Source
if (-not $sevenZip) { $sevenZip = @("$env:ProgramFiles\7-Zip\7z.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1 }
if (-not $sevenZip) { throw "7-Zip is needed to unpack the engines (winget install 7zip.7zip)" }

foreach ($e in $engines) {
  $dest = Join-Path $root $e.Name
  if ((Test-Path $dest) -and -not $Force) { Write-Host "ok      $($e.Name)"; continue }
  $file = Join-Path $cache ([IO.Path]::GetFileName($e.Url))
  if (-not (Test-Path $file)) { Write-Host "get     $($e.Url)"; Invoke-WebRequest $e.Url -OutFile $file -UseBasicParsing }
  $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($e.Sha -and $hash -ne $e.Sha) { Remove-Item $file; throw "$($e.Name): checksum mismatch (got $hash). Download removed." }
  if (-not $e.Sha) { Write-Host "sha256  $($e.Name) $hash  (pin this in fetch-engines.ps1)" }

  $tmp = Join-Path $cache "x-$($e.Name)"
  Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
  if ($file.EndsWith(".msi")) {
    # administrative image: copies the files out of the MSI without installing anything or touching the registry
    $p = Start-Process msiexec.exe -ArgumentList "/a", "`"$file`"", "/qn", "TARGETDIR=`"$tmp`"" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "$($e.Name): msiexec could not unpack (exit $($p.ExitCode))" }
    Get-ChildItem $tmp -Filter *.msi | Remove-Item -Force   # the admin image leaves a copy of the MSI behind
  } else {
    & $sevenZip x $file "-o$tmp" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$($e.Name): could not unpack" }
  }
  # archives often wrap everything in one top folder; use what is inside it
  $inner = Get-ChildItem $tmp
  if ($inner.Count -eq 1 -and $inner[0].PSIsContainer) { $tmp = $inner[0].FullName }

  Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory $dest | Out-Null
  if ($e.Keep -eq "*") { Copy-Item "$tmp\*" $dest -Recurse }
  else { foreach ($k in $e.Keep) { $src = Join-Path $tmp $k; if (Test-Path $src) { Copy-Item $src $dest -Recurse } else { Write-Warning "$($e.Name): $k not in archive" } } }
  foreach ($d in @($e.Drop)) { if ($d) { Remove-Item (Join-Path $dest $d) -Force -ErrorAction SilentlyContinue } } # unused extras
  Write-Host "unpack  $($e.Name) -> $dest"
}
Write-Host "Engines ready in $root"
