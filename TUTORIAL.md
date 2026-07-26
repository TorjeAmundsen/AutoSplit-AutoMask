## Downloads to prepare

- [AutoSplit](https://github.com/Toufool/AutoSplit/releases)

- [LiveSplit.AutoSplitIntegration (Windows only)](https://github.com/Toufool/LiveSplit.AutoSplitIntegration/releases/latest/download/LiveSplit.AutoSplitIntegration.dll)

- [AutoMask](https://github.com/TorjeAmundsen/AutoSplit-AutoMask/releases)

# AutoSplit + OBS Setup

Your game feed must have **no black borders**. AutoMask can adjust for aspect ratio differences, but it cannot detect or correct for black bars or over-cropping. The setup below uses OBS Virtual Cam (the recommended capture method for AutoSplit) with OBS filters doing all cropping and scaling, so that AutoSplit sees exactly your game feed and nothing else.

<img width="960" height="420" alt="Side-by-side comparison of an un-cropped, un-scaled game feed with black bars around it vs a cropped and scaled one" src="https://github.com/user-attachments/assets/0e83e003-b5b1-4332-8f97-7e2d71819e00" />

## 1. Crop your feed with the Crop/Pad filter

Crop your capture source using the **Crop/Pad** filter (right-click your capture source, then **Filters**), **not** with OBS's transform features (dragging to scale, alt-dragging to crop in the preview window). Transforms only change how the source is displayed on your canvas - the source itself, and therefore what OBS Virtual Cam outputs, stays uncropped. If your feed is currently set up with transforms, right-click the source and press **Reset transform** in the **Transform** menu, then redo it with filters.

Set the crop values with the game running so you can see exactly where the picture ends. Games render at different resolutions, so the correct crop values vary per game and per capture card.

## 2. Scale your feed with the Scaling/Aspect ratio filter

After cropping, add a **Scaling/Aspect ratio** filter (below Crop/Pad in the filter list) and set it to a 4:3 resolution. Scaling the feed to a known 4:3 resolution is required to crop your feed correctly in AutoSplit later, so you might as well set it to the 4:3 resolution that fills your canvas vertically - **1440x1080** on a 1920x1080 canvas, **960x720** on a 1280x720 canvas - unless you have an OBS layout that doesn't display your game at max size, such as an overlay with a border around your game feed. Picking the generic "4:3" option still works, but typing the resolution in manually is optimal.

Set the scale filtering to **Area**, or **Point** for an even harsher/more pixelated look. Avoid the other scale filtering methods.

If you do not scale to a 4:3 resolution, you will have to do your own math to crop your feed in AutoSplit in [Step 7](<TUTORIAL#7. Crop the capture region in AutoSplit>).

## 3. Color correct your feed (optional, but do it now if ever)

If you want to color correct your feed, do it **before** setting up AutoSplit - changing your colors later forces you to remake all your AutoSplit images.

For this I made [AutoLUT](https://torjeamundsen.github.io/AutoLUT/), which generates an OBS-compatible LUT that corrects your Wii or N64 capture to match the console's true output colors, applied with OBS's built-in **Apply LUT** filter. It runs in your browser, and its in-app guide walks you through the whole process, including the same crop/scale filter setup as above. Filter order on your source should be: **Apply LUT**, then **Crop/Pad**, then **Scaling/Aspect ratio**.

## 4. Set up OBS Virtual Camera

<img width="396" height="199" alt="OBS Virtual Cam settings window showing the correct settings" src="https://github.com/user-attachments/assets/5c01052e-1a73-4542-8e5b-738f453944e6" />


In OBS, click the gear icon next to **Start Virtual Camera** and set:

- **Output Type**: **Source**
- **Output Selection**: your capture card source

Then start the Virtual Camera. With Output Type set to Source, the Virtual Cam outputs your capture source with all its filters applied.

## 5. Install AutoSplit and the LiveSplit integration

Download [AutoSplit](https://github.com/Toufool/AutoSplit) and the [LiveSplit AutoSplit Integration plugin](https://github.com/Toufool/LiveSplit.AutoSplitIntegration#autosplit-integration--), and set up the integration as per its instructions. The integration lets AutoSplit control your LiveSplit timer directly.

Linux users: You'll have to rely on AutoSplit via its hotkey settings instead, since this plugin is Windows only.

## 6. Set AutoSplit's capture method

<img width="288" height="361" alt="AutoSplit's settings window showing correct 'Capture method' and 'Capture device'" src="https://github.com/user-attachments/assets/fe91d02c-8542-428d-9f50-662e186c0a26" />


In AutoSplit, open **Settings**, go to the capture settings, and set:

- **Capture Method**: **Video Capture Device**
- **Capture Device**: **OBS Virtual Camera**

## 7. Crop the capture region in AutoSplit

<img width="898" height="339" alt="Side-by-side comparison of an un-cropped 1920x1080 OBS Virtual Cam feed in AutoSplit with black bars on the sides vs a correctly cropped one" src="https://github.com/user-attachments/assets/af58d019-e734-4866-95bf-70c3fa94d8ee" />


OBS Virtual Cam displays your selected source centered in OBS's canvas resolution, so you need to crop in AutoSplit so it only sees your game feed. With a 1920x1080 canvas and a 1440x1080 scaled feed, the feed sits centered with 240 pixels of padding on each side (half of the missing 480), so set:

- **X**: 240, **Width**: 1440 (1920x1080 canvas)
- **X**: 160, **Width**: 960 (1280x720 canvas)

In general: X = (canvas width - feed width) / 2, Width = feed width. Leave Y at 0 and Height at the full canvas height.

## 8. Set a screenshot output folder

Click **Browse...** in AutoSplit and select an output folder for your screenshots. AutoSplit reads every image in this folder, so it must only contain your split images. You can point AutoMask's output at this same folder - when you use **Save all**, AutoMask automatically moves your base screenshots into a `base` subfolder so only the generated split images remain.

From here, follow the AutoMask steps below.

# Using AutoMask

## 9. Select a preset

Select a preset from the dropdown in the top-right.

## 10. Take a screenshot for each split

Take a screenshot for each split **using AutoSplit**, in split order:

- If your preset has savestates, press **Copy savestates** in the bottom right to copy them to your clipboard. In the case of .gzs OoT savestates, paste them on your SD card, then load each one in gz and screenshot it. The savestates are named in split order.
- If any splits have extra instructions, the **Instructions** button next to **Copy savestates** becomes available - follow those for the splits that have them.
- If your preset has no savestates, obtain your screenshots manually.

## 11. Load your base images

Click **Load base image(s)** and select all your screenshots. Screenshots taken in order by AutoSplit automatically match the preset's split order.

## 12. Set the output folder

Click the folder icon next to the output path field to choose an output folder - typically the same folder AutoSplit is already pointed at.

## 13. Save all

Click **Save all**. This masks and saves every split at once with the correct auto-generated filenames. The button becomes available when the number of loaded input images matches the number of splits in the preset.

If your output folder is the same folder your base screenshots are in, **Save all** automatically moves the base images to a `base` subfolder, since AutoSplit reads every image in the folder it's pointed at.

That's it - your AutoSplit folder now contains a complete, working image set.

## Masking image by image

If your input images don't line up with the preset's split order, or you want to redo a single split:

1. Use the dropdowns beneath the image previews to pair each input image with its corresponding mask, or step through them with the arrow buttons next to the dropdowns.
2. Click **Save output** to save the current output with the automatically generated filename.

Note that only **Save all** moves base images to the `base` subfolder - when saving image by image, move your base screenshots out of the AutoSplit folder yourself.
