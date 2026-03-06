$ErrorActionPreference = "Stop"

$project = "AutoMask/AutoMask.csproj"
$output = "$PSScriptRoot/build"

Write-Host "Building AutoMask (Release, win-x64, ReadyToRun single file)..."

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishReadyToRun=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $output

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Build complete. Output: $output"
