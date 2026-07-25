# Download

Download the [latest release](/../../releases/latest) for your platform. The builds are self-contained Native AOT executables - no .NET runtime required.

# AutoMask for AutoSplit

This program uses preset split sequences and masks to automatically generate working [AutoSplit](https://github.com/Toufool/AutoSplit) images from your original screenshots. The generated images are automatically named with the correct filenames, including threshold levels, delay times, dummy tags, etc., all pre-defined by the [preset](#presets).

A quick and easy way to instantly set up AutoSplit, given your desired category has an AutoMask preset.

<img width="696" height="444" alt="AutoMask_NGWaCml5r6" src="https://github.com/user-attachments/assets/e29334ca-0777-4ef5-8dd6-1b420462d738" />


# How to Use
I recommend following the AutoSplit + OBS setup guide even if you already have AutoSplit set up. Small differences to your current setup may be significant for AutoMask to work correctly.

## AutoSplit + OBS Setup

Your game feed must have **no black borders**. AutoMask can adjust for aspect ratio differences, but it cannot detect or correct for black bars or over-cropping. The setup below uses OBS Virtual Cam (the recommended capture method for AutoSplit) with OBS filters doing all cropping and scaling, so that AutoSplit sees exactly your game feed and nothing else.

### 1. Crop your feed with the Crop/Pad filter

Crop your capture source using the **Crop/Pad** filter (right-click your capture source, then **Filters**), **not** with OBS's transform features (dragging to scale, alt-dragging to crop in the preview window). Transforms only change how the source is displayed on your canvas - the source itself, and therefore what OBS Virtual Cam outputs, stays uncropped. If your feed is currently set up with transforms, right-click the source and press **Reset transform** in the **Transform** menu, then redo it with filters.

Set the crop values with the game running so you can see exactly where the picture ends. Games render at different resolutions, so the correct crop values vary per game and per capture card.

### 2. Scale your feed with the Scaling/Aspect ratio filter

After cropping, add a **Scaling/Aspect ratio** filter (below Crop/Pad in the filter list) and set it to a 4:3 resolution. Scaling the feed to a known 4:3 resolution is required to crop your feed correctly in AutoSplit later, so you might as well set it to the 4:3 resolution that fills your canvas vertically - **1440x1080** on a 1920x1080 canvas, **960x720** on a 1280x720 canvas - unless you have an OBS layout that doesn't display your game at max size, such as an overlay with a border around your game feed. Picking the generic "4:3" option still works, but typing the resolution in manually is optimal.

Set the scale filtering to **Area**, or **Point** for an even harsher/more pixelated look. Avoid the other scale filtering methods.

### 3. Color correct your feed (optional, but do it now if ever)

If you want to color correct your feed, do it **before** setting up AutoSplit - changing your colors later forces you to remake all your AutoSplit images.

For this I made [AutoLUT](https://torjeamundsen.github.io/AutoLUT/), which generates an OBS-compatible LUT that corrects your Wii or N64 capture to match the console's true output colors, applied with OBS's built-in **Apply LUT** filter. It runs in your browser, and its in-app guide walks you through the whole process, including the same crop/scale filter setup as above. Filter order on your source should be: **Apply LUT**, then **Crop/Pad**, then **Scaling/Aspect ratio**.

### 4. Set up OBS Virtual Camera

In OBS, click the gear icon next to **Start Virtual Camera** and set:

- **Output Type**: **Source**
- **Output Selection**: your capture card source

Then start the Virtual Camera. With Output Type set to Source, the Virtual Cam outputs your capture source with all its filters applied.

### 5. Install AutoSplit and the LiveSplit integration

Download [AutoSplit](https://github.com/Toufool/AutoSplit) and the [LiveSplit AutoSplit Integration plugin](https://github.com/Toufool/LiveSplit.AutoSplitIntegration#autosplit-integration--), and set up the integration as per its instructions. The integration lets AutoSplit control your LiveSplit timer directly.

### 6. Set AutoSplit's capture method

In AutoSplit, open **Settings**, go to the capture settings, and set:

- **Capture Method**: **Video Capture Device**
- **Capture Device**: **OBS Virtual Camera**

### 7. Crop the capture region in AutoSplit

OBS Virtual Cam displays your selected source centered in OBS's canvas resolution, so you need to crop in AutoSplit so it only sees your game feed. With a 1920x1080 canvas and a 1440x1080 scaled feed, the feed sits centered with 240 pixels of padding on each side (half of the missing 480), so set:

- **X**: 240, **Width**: 1440 (1920x1080 canvas)
- **X**: 160, **Width**: 960 (1280x720 canvas)

In general: X = (canvas width - feed width) / 2, Width = feed width. Leave Y at 0 and Height at the full canvas height.

### 8. Set a screenshot output folder

Click **Browse...** in AutoSplit and select an output folder for your screenshots. AutoSplit reads every image in this folder, so it must only contain your split images. You can point AutoMask's output at this same folder - when you use **Save all**, AutoMask automatically moves your base screenshots into a `base` subfolder so only the generated split images remain.

From here, follow the AutoMask instructions below.

## Using AutoMask

1. Select a preset from the dropdown in the top-right.
2. Take a screenshot for each split **using AutoSplit**, in split order:
   - If your preset has savestates, press **Copy savestates** in the bottom right to copy them to your clipboard. In the case of .gzs OoT savestates, paste them on your SD card, then load each one in gz and screenshot it. The savestates are named in split order.
   - If any splits have extra instructions, the **Instructions** button next to **Copy savestates** becomes available - follow those for the splits that have them.
   - If your preset has no savestates, obtain your screenshots manually.
3. Click **Load base image(s)** and select all your screenshots. Screenshots taken in order by AutoSplit automatically match the preset's split order.
4. Click the folder icon next to the output path field to choose an output folder - typically the same folder AutoSplit is pointed at.
5. Click **Save all**. This masks and saves every split at once with the correct auto-generated filenames. The button becomes available when the number of loaded input images matches the number of splits in the preset.

If your output folder is the same folder your base screenshots are in, **Save all** automatically moves the base images to a `base` subfolder, since AutoSplit reads every image in the folder it's pointed at.

That's it - your AutoSplit folder now contains a complete, working image set.

### Masking image by image

If your input images don't line up with the preset's split order, or you want to redo a single split:

1. Use the dropdowns beneath the image previews to pair each input image with its corresponding mask, or step through them with the arrow buttons next to the dropdowns.
2. Click **Save output** to save the current output with the automatically generated filename.

Note that only **Save all** moves base images to the `base` subfolder - when saving image by image, move your base screenshots out of the AutoSplit folder yourself.

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
