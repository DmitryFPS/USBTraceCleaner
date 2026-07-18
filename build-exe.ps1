param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "USBTraceCleaner\USBTraceCleaner.csproj"
$publishDir = Join-Path $root "USBTraceCleaner\bin\$Configuration\net8.0-windows\$Runtime\publish"
$docsDir = Join-Path $root "docs"

function Get-EngineeringGuideHtml {
    $file = Get-ChildItem -LiteralPath $docsDir -Filter "USBTraceCleaner_*.html" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $file) {
        throw "Engineering guide HTML not found in: $docsDir (expected USBTraceCleaner_*.html)"
    }
    return $file
}

function Get-EngineeringGuidePdf {
    $file = Get-ChildItem -LiteralPath $docsDir -Filter "USBTraceCleaner_*.pdf" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $file) {
        throw "Engineering guide PDF not found in: $docsDir (expected USBTraceCleaner_*.pdf)"
    }
    return $file
}

function Ensure-EngineeringGuidePdf {
    if (-not (Test-Path -LiteralPath $docsDir)) {
        throw "docs directory not found: $docsDir"
    }

    $guideHtml = Get-EngineeringGuideHtml
    $guidePdfPath = [System.IO.Path]::ChangeExtension($guideHtml.FullName, ".pdf")

    $edgeCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    )
    $edge = $edgeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $edge) {
        throw "Microsoft Edge not found. Cannot regenerate engineering guide PDF from HTML."
    }

    Write-Host "Generating engineering guide PDF from: $($guideHtml.Name)"
    & $edge --headless --disable-gpu --no-pdf-header-footer --print-to-pdf="$guidePdfPath" "$($guideHtml.FullName)"
    Start-Sleep -Seconds 1

    if (-not (Test-Path -LiteralPath $guidePdfPath) -or (Get-Item -LiteralPath $guidePdfPath).Length -lt 1000) {
        throw "Failed to generate engineering guide PDF: $guidePdfPath"
    }

    return (Get-Item -LiteralPath $guidePdfPath)
}

$guidePdf = Ensure-EngineeringGuidePdf

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
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Published exe not found: $publishedExe"
}

$publishedGuide = Get-ChildItem -LiteralPath $publishDir -Filter "USBTraceCleaner_*.pdf" -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $publishedGuide) {
    $dest = Join-Path $publishDir $guidePdf.Name
    Copy-Item -LiteralPath $guidePdf.FullName -Destination $dest -Force
    $publishedGuide = Get-Item -LiteralPath $dest
}

if (-not $publishedGuide -or $publishedGuide.Length -lt 1000) {
    throw "Engineering guide PDF missing next to exe in: $publishDir"
}

$pdfSignature = [System.Text.Encoding]::ASCII.GetString(
    [System.IO.File]::ReadAllBytes($publishedGuide.FullName), 0, 4)
if ($pdfSignature -ne "%PDF") {
    throw "Generated file does not have a PDF signature: $($publishedGuide.FullName)"
}

Write-Host "Published to: $publishDir"
Write-Host "Portable exe: $publishedExe"
Write-Host "Engineering guide PDF: $($publishedGuide.FullName)"
Write-Host "Run as Administrator."
