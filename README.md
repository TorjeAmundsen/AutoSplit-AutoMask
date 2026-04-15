# Download

Download the [latest release](/../../releases/latest) for your platform. The builds are self-contained Native AOT executables - no .NET runtime required.

# AutoMask for AutoSplit

This program uses preset split sequences and masks to automatically generate working [AutoSplit](https://github.com/Toufool/AutoSplit) images from your original screenshots. The generated images are automatically named with the correct filenames, including threshold levels, delay times, dummy tags, etc., all pre-defined by the [preset](#presets).

A quick and easy way to instantly set up AutoSplit, given your desired category has an AutoMask preset.

<img width="836" height="506" alt="AutoMask_O6bpcmfpMB" src="https://github.com/user-attachments/assets/b51b964d-52cc-47b0-8ff2-23716a66e83b" />



## How to Use

> Your game feed must have **no black borders**. AutoMask can adjust for aspect ratio differences, but it cannot detect or correct for black bars or over-cropping.

1. Click **Load input image(s)** and select the screenshots you want to mask.
2. Select a preset from the dropdown in the top-right.
3. Use the dropdowns beneath the image previews to pair each input image with its corresponding mask.
4. Click **Set output** to choose an output folder.
5. Click **Save masked image** to save the current output with the automatically generated filename.
6. Use the arrow buttons next to the dropdowns to step through inputs and masks.

The splits in a preset are ordered as defined in its `preset.json`. Input images are ordered alphabetically, so if your screenshots were taken in order by AutoSplit, they will match the preset order when loaded.

### Save All Split Images

If you have the same number of input images loaded as there are splits in the selected preset, the **Save all split images** button becomes available. This masks and saves all splits at once, assuming the input image order matches the split order.

## Live Output Tester

Click **Live tester** to open a real-time comparison window. It captures a live video feed from a window, webcam, or screen region and compares it against the current output image using the same L2 Norm algorithm as AutoSplit. See [TESTER.md](TESTER.md) for details.

## Presets

Presets define the sequence of splits and their mask images, thresholds, timing, and other settings for a specific game and category. They are stored in the `presets/` folder next to the executable.

Click **Edit** next to the preset dropdown to open the preset editor, where you can create new presets, modify existing ones, and manage splits. See [PRESETS.md](PRESETS.md) for full documentation on the preset editor.

## Building from Source

Requires .NET 10 SDK. Release builds use Native AOT compilation.

```sh
# Build for your current OS
./build.ps1

# Build all platforms (uses Docker for cross-OS AOT)
./build.ps1 --all
```

The Linux cross-compilation requires Docker to be running.
