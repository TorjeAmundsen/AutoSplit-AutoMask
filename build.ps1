$ErrorActionPreference = "Stop"

$project = "AutoMask/AutoMask.csproj"
$dockerImage = "automask-build"

$rids = @("win-x64", "linux-x64")

$debug = $args -contains "--debug"

$csprojVersion = ([xml](Get-Content "$PSScriptRoot/$project")).Project.PropertyGroup.Version
$appVersion = "v$csprojVersion"

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

        $dockerPublishArgs = "dotnet publish $project -c Release -r $rid --self-contained true /p:PublishAot=true -o /src/build/$rid"
        if ($debug) {
            $dockerPublishArgs += " /p:NativeDebugSymbols=true /p:StripSymbols=false"
        }

        docker run --rm `
            -v "${PSScriptRoot}:/src" `
            -w /src `
            $dockerImage `
            sh -c $dockerPublishArgs `
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

    if ($debug) {
        $dotnetArgs += "/p:NativeDebugSymbols=true"
        $dotnetArgs += "/p:StripSymbols=false"
    }

    dotnet @dotnetArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $rid with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    # Remove debug symbols from output (unless --debug specified)
    if (-not $debug) {
        Get-ChildItem "$output" -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item
    }

    Write-Host "  Output: $output"
    return $output
}

function ZipBuild($outputPath, $rid) {
    $zipName = "$PSScriptRoot/build/AutoMask-$appVersion-$rid.zip"

    if (Test-Path $zipName) { Remove-Item $zipName }

    Compress-Archive -Path "$outputPath/*" -DestinationPath $zipName
    Write-Host "  Zipped: $zipName"
}

function ZipPresets() {
    $zipName = "$PSScriptRoot/build/presets-and-splits-$appVersion.zip"

    if (Test-Path $zipName) { Remove-Item $zipName }

    $presetsPath = "$PSScriptRoot/AutoMask/presets"
    $splitsPath = "$PSScriptRoot/AutoMask/splits"

    $tempDir = "$PSScriptRoot/build/_presets-temp"
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    New-Item -ItemType Directory -Path "$tempDir/presets" | Out-Null
    New-Item -ItemType Directory -Path "$tempDir/splits" | Out-Null

    Copy-Item -Path "$presetsPath/*" -Destination "$tempDir/presets" -Recurse
    Copy-Item -Path "$splitsPath/*" -Destination "$tempDir/splits" -Recurse

    Compress-Archive -Path "$tempDir/*" -DestinationPath $zipName
    Remove-Item $tempDir -Recurse -Force

    Write-Host "  Zipped: $zipName"
}

if ($args -contains "--presets") {
    if (-not (Test-Path "$PSScriptRoot/build")) { New-Item -ItemType Directory -Path "$PSScriptRoot/build" | Out-Null }
    ZipPresets
    Write-Host "Build complete."
    exit 0
}

if ($args -contains "--all") {
    foreach ($rid in $rids) {
        $out = Build $rid
        ZipBuild $out $rid
    }
    ZipPresets
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
