$ErrorActionPreference = "Stop"

$project = "AutoMask/AutoMask.csproj"
$dockerImage = "automask-build"

$rids = @("win-x64", "linux-x64")

function Build($rid) {
    $output = "$PSScriptRoot/build/$rid"

    if (Test-Path $output) { Remove-Item $output -Recurse -Force }

    Write-Host "Building AutoMask ($rid)..."

    # Native AOT requires building on the target OS.
    # Use Docker for Linux AOT builds when running on Windows.
    $currentOs = if ($IsWindows) { "win" } elseif ($IsLinux) { "linux" } else { "osx" }
    $needsDocker = -not $rid.StartsWith($currentOs)

    if ($needsDocker -and $rid.StartsWith("linux")) {
        Write-Host "  Using Docker for cross-OS AOT compilation..."

        docker build -t $dockerImage "$PSScriptRoot" | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker image build failed with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }

        docker run --rm `
            -v "${PSScriptRoot}:/src" `
            -w /src `
            $dockerImage `
            dotnet publish $project -c Release -r $rid --self-contained true /p:PublishAot=true -o /src/build/$rid `
            | Out-Host

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker build failed for $rid with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }

        # Remove debug symbols from output
        Get-ChildItem "$output" -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item

        Write-Host "  Output: $output"
        return $output
    }

    $dotnetArgs = @(
        "publish", $project,
        "-c", "Release",
        "-r", $rid,
        "--self-contained", "true",
        "/p:PublishAot=true",
        "-o", $output
    )

    dotnet @dotnetArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $rid with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    # Remove debug symbols from output
    Get-ChildItem "$output" -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item

    Write-Host "  Output: $output"
    return $output
}

function ZipBuild($outputPath, $rid) {
    $zipName = "$PSScriptRoot/build/AutoMask-$rid.zip"

    if (Test-Path $zipName) { Remove-Item $zipName }

    Compress-Archive -Path "$outputPath/*" -DestinationPath $zipName
    Write-Host "  Zipped: $zipName"
}

if ($args -contains "--all") {
    foreach ($rid in $rids) {
        $out = Build $rid
        ZipBuild $out $rid
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

Build $rid
Write-Host "Build complete."
