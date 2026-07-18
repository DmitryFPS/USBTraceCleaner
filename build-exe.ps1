param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "USBTraceCleaner\USBTraceCleaner.csproj"
$publishDir = Join-Path $root "USBTraceCleaner\bin\$Configuration\net8.0-windows\$Runtime\publish"
$docsDir = Join-Path $root "docs"
$guideHtml = Join-Path $docsDir "USBTraceCleaner_Инженерное_руководство.html"
$guidePdf = Join-Path $docsDir "USBTraceCleaner_Инженерное_руководство.pdf"

function Ensure-EngineeringGuidePdf {
    if (-not (Test-Path $guideHtml)) {
        throw "Engineering guide HTML not found: $guideHtml"
    }

    $edgeCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    )
    $edge = $edgeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $edge) {
        throw "Microsoft Edge not found. Cannot regenerate engineering guide PDF from HTML."
    }

    Write-Host "Generating engineering guide PDF..."
    & $edge --headless --disable-gpu --no-pdf-header-footer --print-to-pdf="$guidePdf" "$guideHtml"
    if (-not (Test-Path $guidePdf) -or (Get-Item $guidePdf).Length -lt 1000) {
        throw "Failed to generate engineering guide PDF: $guidePdf"
    }
}

Ensure-EngineeringGuidePdf

Write-Host "Publishing USBTraceCleaner ($Configuration, $Runtime)..."
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishDir "USBTraceCleaner.exe"
$publishedGuide = Join-Path $publishDir "USBTraceCleaner_Инженерное_руководство.pdf"

if (-not (Test-Path $publishedExe)) {
    throw "Published exe not found: $publishedExe"
}

if (-not (Test-Path $publishedGuide)) {
    Copy-Item $guidePdf $publishedGuide -Force
}

if (-not (Test-Path $publishedGuide) -or (Get-Item $publishedGuide).Length -lt 1000) {
    throw "Engineering guide PDF missing next to exe: $publishedGuide"
}

$pdfSignature = [System.Text.Encoding]::ASCII.GetString(
    [System.IO.File]::ReadAllBytes($publishedGuide), 0, 4)
if ($pdfSignature -ne "%PDF") {
    throw "Generated file does not have a PDF signature: $publishedGuide"
}

Write-Host "Published to: $publishDir"
Write-Host "Portable exe: $publishedExe"
Write-Host "Engineering guide PDF: $publishedGuide"
Write-Host "Run as Administrator."
