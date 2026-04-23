# Download

Download the [latest release](/../../releases/latest) for your platform. The builds are self-contained Native AOT executables - no .NET runtime required.

# AutoMask for AutoSplit

This program uses preset split sequences and masks to automatically generate working [AutoSplit](https://github.com/Toufool/AutoSplit) images from your original screenshots. The generated images are automatically named with the correct filenames, including threshold levels, delay times, dummy tags, etc., all pre-defined by the [preset](#presets).

A quick and easy way to instantly set up AutoSplit, given your desired category has an AutoMask preset.

<img width="696" height="444" alt="AutoMask_NGWaCml5r6" src="https://github.com/user-attachments/assets/e29334ca-0777-4ef5-8dd6-1b420462d738" />


## How to Use

> Your game feed must have **no black borders**. AutoMask can adjust for aspect ratio differences, but it cannot detect or correct for black bars or over-cropping. For correct behavior, please set this up with OBS Virtual Cam (the recommended capture method for AutoSplit) and use the Crop/Pad filter in combination with the Scaling/Aspect ratio filter to crop and scale your feed. Do **not** use OBS's transform features (ctrl-dragging etc). If you have your feed set up with transform features, right click your source and press **Reset transform** in the **Transform** menu and use the filters method instead.

1. Select a preset from the dropdown in the top-right.
2. If your preset has savestates, copy them by pressing **Copy savestates** in the bottom right. If not, obtain your screenshots manually and skip to step 5.
3. Open AutoSplit and set an output folder to save your screenshots. You have to take your screenshots of the savestates using AutoSplit.
4. Load your savestates and screenshot them in order. Follow instructions for select splits by clicking **Instructions** if they have instructions.
5. Click **Load input image(s)** and select all the screenshots you want to mask.
6. Use the dropdowns beneath the image previews to pair each input image with its corresponding mask.
7. Click **Set output** to choose an output folder.
8. Click **Save masked image** to save the current output with the automatically generated filename.
9. Use the arrow buttons next to the dropdowns to step through inputs and masks.

The splits in a preset are ordered as defined in its `preset.json`. Input images are ordered alphabetically, so if your screenshots were taken in order by AutoSplit, they will match the preset order when loaded.

### Save All Split Images

If you have the same number of input images loaded as there are splits in the selected preset, the **Save all split images** button becomes available. This masks and saves all splits at once, assuming the input image order matches the split order.

## Live Output Tester

Click **Live tester** to open a real-time comparison window. It captures a live video feed from a window, webcam, or screen region and compares it against the current output image using the same L2 Norm algorithm as AutoSplit. See [TESTER.md](TESTER.md) for details.

## Presets

Presets define the sequence of splits and their mask images, thresholds, timing, and other settings for a specific game and category. They are stored in the `presets/` folder next to the executable.

Click **Edit** next to the preset dropdown to open the preset editor, where you can create new presets, modify existing ones, and manage splits. See [PRESETS.md](PRESETS.md) for full documentation on the preset editor.

## Savestates

A preset can bundle savestate files alongside its splits, letting you jump straight to the frame where each split image triggers in order to take base image screenshots easily. Each split can reference a single savestate file of any type (`.gzs`, `.savestate`, `.sav`, etc.) - the format isn't validated, so whatever your game/platform supports will work.

When a preset with savestates is selected, the status bar shows **Savestates available** and enables the **Copy savestates** button. Clicking it copies all savestate files in the current preset to the clipboard, renamed with the pattern `{index}_{split name}.{ext}` so you can paste them into your desired folder - such as your SD card - in split order. Savestate files are linked per-split in the preset editor - see [PRESETS.md](PRESETS.md#savestate) for details.

Each split with a savestate can also have free-form **savestate instructions** attached. If any split in the current preset has instructions, the **Instructions** button next to **Copy savestates** becomes enabled and opens a window listing every split's instructions in order.

## Building from Source

Requires .NET 10 SDK. Release builds use Native AOT compilation.

```sh
# Build for your current OS
./build.ps1

# Build all platforms (uses Docker for cross-OS AOT)
./build.ps1 --all
```

The Linux cross-compilation requires Docker to be running.
