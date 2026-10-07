# Download

Download the [latest release](/../../releases/latest) for your platform: `win-x64`, `linux-x64` or `macos-universal` (one app for Apple Silicon and Intel Macs). The builds are self-contained Native AOT executables - no .NET runtime required.

On macOS, unzip it and move `AutoMask.app` to your Applications folder. macOS blocks the first start because the app isn't notarized: run `xattr -dr com.apple.quarantine /Applications/AutoMask.app`, or allow it in System Settings > Privacy & Security > Open Anyway. The live tester asks for camera access the first time you pick a capture source.

# AutoMask for AutoSplit

This program uses preset split sequences and masks to automatically generate working [AutoSplit](https://github.com/Toufool/AutoSplit) images from your original screenshots. The generated images are automatically named with the correct filenames, including threshold levels, delay times, dummy tags, etc., all pre-defined by the [preset](#presets).

A quick and easy way to instantly set up AutoSplit, given your desired category has an AutoMask preset.

<img width="696" height="444" alt="AutoMask_NGWaCml5r6" src="https://github.com/user-attachments/assets/e29334ca-0777-4ef5-8dd6-1b420462d738" />


# How to Use
See the [TUTORIAL](TUTORIAL.md) for the full AutoSplit + OBS setup and step-by-step AutoMask walkthrough. I recommend following it even if you already have AutoSplit set up - small differences to your current setup may be significant for AutoMask to work correctly.

## Live Output Tester

Click **Live tester** to open a real-time comparison window. It captures a live video feed from a webcam source (such as OBS Virtual Cam) and compares it against the current output image using the same L2 Norm algorithm as AutoSplit. See [TESTER](TESTER.md) for details.

## Presets

Presets define the sequence of splits and their mask images, thresholds, timing, and other settings for a specific game and category. They are stored in the `presets/` folder next to the executable. On macOS they are in `~/Library/Application Support/AutoMask/presets` instead (with `splits/` and `config/` next to it), since the app bundle can't be written to. The presets that come with the app are copied there on first launch, and presets a new version adds are copied the first time that version starts. Files already there are never overwritten (apart from the JSON schema files), so your edits stay, but a bundled preset you deleted comes back after an update.

Click **Edit** next to the preset dropdown to open the preset editor, where you can create new presets, modify existing ones, and manage splits. See [PRESETS](PRESETS.md) for full documentation on the preset editor.

## Savestates

A preset can bundle savestate files alongside its splits, letting you jump straight to the frame where each split image triggers in order to take base image screenshots easily. Each split can reference a single savestate file of any type (`.gzs`, `.savestate`, `.sav`, etc.) - the format isn't validated, so whatever your game/platform supports will work.

When a preset with savestates is selected, the status bar shows **Savestates available** and enables the **Copy savestates** button. Clicking it copies all savestate files in the current preset to the clipboard, renamed with the pattern `{index}_{split name}.{ext}` so you can paste them into your desired folder - such as your SD card - in split order. Savestate files are linked per-split in the preset editor - see [PRESETS](PRESETS.md#savestate) for details.

Each split with a savestate can also have free-form **savestate instructions** attached. If any split in the current preset has instructions, the **Instructions** button next to **Copy savestates** becomes enabled and opens a window listing every split's instructions in order.

## Building from Source

Requires .NET 10 SDK. Release builds use Native AOT compilation.

```sh
# Build for your current OS
./build.ps1

# Build all platforms (uses Docker for cross-OS AOT)
./build.ps1 --all
```

The Linux cross-compilation requires Docker to be running. The `macos-universal` build needs a Mac, because Apple's SDK may only be used there; `--all` skips it on Windows and Linux. It publishes `osx-arm64` and `osx-x64`, then `macos/bundle.sh` merges them into one `AutoMask.app`.
