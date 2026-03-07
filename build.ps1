$ErrorActionPreference = "Stop"

$project = "AutoMask/AutoMask.csproj"
$outputSelfContained = "$PSScriptRoot/build/self-contained"
$outputFrameworkDependent = "$PSScriptRoot/build/framework-dependent"

Write-Host "Building AutoMask (self-contained)..."

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishReadyToRun=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outputSelfContained

if ($LASTEXITCODE -ne 0) {
    Write-Error "Self-contained build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Building AutoMask (framework-dependent)..."

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outputFrameworkDependent

if ($LASTEXITCODE -ne 0) {
    Write-Error "Framework-dependent build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Build complete."
Write-Host "  Self-contained:      $outputSelfContained"
Write-Host "  Framework-dependent: $outputFrameworkDependent"
