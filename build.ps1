$ErrorActionPreference = "Stop"

$project = "AutoMask/AutoMask.csproj"

$rids = @("win-x64", "linux-x64")

foreach ($rid in $rids) {
    $outputSelfContained = "$PSScriptRoot/build/$rid/self-contained"
    $outputFrameworkDependent = "$PSScriptRoot/build/$rid/framework-dependent"

    Write-Host "Building AutoMask ($rid, self-contained)..."

    dotnet publish $project `
        -c Release `
        -r $rid `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:PublishReadyToRun=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        -o $outputSelfContained

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Self-contained build failed for $rid with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host "Building AutoMask ($rid, framework-dependent)..."

    dotnet publish $project `
        -c Release `
        -r $rid `
        --self-contained false `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        -o $outputFrameworkDependent

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Framework-dependent build failed for $rid with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host "  Self-contained:      $outputSelfContained"
    Write-Host "  Framework-dependent: $outputFrameworkDependent"
}

Write-Host "Build complete."
