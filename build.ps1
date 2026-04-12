$ErrorActionPreference = "Stop"

$project = "AutoMask/AutoMask.csproj"

$rids = @("win-x64", "linux-x64")

function Build($rid, $selfContained) {
    $type = if ($selfContained) { "self-contained" } else { "framework-dependent" }
    $output = "$PSScriptRoot/build/$rid/$type"

    Write-Host "Building AutoMask ($rid, $type)..."

    # Native AOT requires building on the target OS.
    # Use Docker for Linux AOT builds when running on Windows.
    $currentOs = if ($IsWindows) { "win" } elseif ($IsLinux) { "linux" } else { "osx" }
    $needsDocker = $selfContained -and -not $rid.StartsWith($currentOs)

    if ($needsDocker -and $rid.StartsWith("linux")) {
        Write-Host "  Using Docker for cross-OS AOT compilation..."

        docker run --rm `
            -v "${PSScriptRoot}:/src" `
            -w /src `
            mcr.microsoft.com/dotnet/sdk:10.0 `
            bash -c "apt-get update && apt-get install -y clang zlib1g-dev && dotnet publish $project -c Release -r $rid --self-contained true /p:PublishAot=true -o /src/build/$rid/$type" `
            | Out-Host

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker build failed for $rid ($type) with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }

        Write-Host "  Output: $output"
        return $output
    }

    $dotnetArgs = @(
        "publish", $project,
        "-c", "Release",
        "-r", $rid,
        "--self-contained", ($selfContained ? "true" : "false"),
        "-o", $output
    )

    if ($selfContained) {
        $dotnetArgs += "/p:PublishAot=true"
    } else {
        $dotnetArgs += "/p:PublishAot=false"
        $dotnetArgs += "/p:PublishSingleFile=true"
    }

    dotnet @dotnetArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $rid ($type) with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host "  Output: $output"
    return $output
}

function ZipBuild($outputPath, $rid, $selfContained) {
    $type = if ($selfContained) { "self-contained" } else { "framework-dependent" }
    $zipName = "$PSScriptRoot/build/AutoMask-$rid-$type.zip"

    if (Test-Path $zipName) { Remove-Item $zipName }

    Compress-Archive -Path "$outputPath/*" -DestinationPath $zipName
    Write-Host "  Zipped: $zipName"
}

if ($args -contains "--all") {
    foreach ($rid in $rids) {
        foreach ($selfContained in @($true, $false)) {
            $out = Build $rid $selfContained
            ZipBuild $out $rid $selfContained
        }
    }
    Write-Host "Build complete."
    exit 0
}

# Interactive menu
Write-Host "Select OS:"
for ($i = 0; $i -lt $rids.Count; $i++) {
    Write-Host "  [$($i + 1)] $($rids[$i])"
}
$ridChoice = Read-Host "Choice"
$rid = $rids[[int]$ridChoice - 1]

Write-Host ""
Write-Host "Select build type:"
Write-Host "  [1] Self-contained"
Write-Host "  [2] Framework-dependent"
$typeChoice = Read-Host "Choice"
$selfContained = $typeChoice -eq "1"

Build $rid $selfContained
Write-Host "Build complete."
