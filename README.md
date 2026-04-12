## Download

- Download the [latest version](/../../releases/latest)

# AutoMask for AutoSplit

This program uses preset split sequences and masks to automatically generate working AutoSplit images from your original screenshots. The generated images will be automatically named with adequate filenames, including threshold levels, delay times, dummy tags, etc. all pre-defined by the [preset](#presets).

Quick and easy way to instantly set up AutoSplit, given your desired category has an AutoMask preset.

<img width="836" height="506" alt="AutoMask_O6bpcmfpMB" src="https://github.com/user-attachments/assets/b51b964d-52cc-47b0-8ff2-23716a66e83b" />

# How to use

First, make sure the game feed you use for AutoSplit has _no black borders_ in its output, your feed needs to be cropped correctly for this to work. It will adjust automatically if your aspect ratio is off, but it can't detect black bars around your feed and correct for them, nor will it correct for your feed being over-cropped.

Select the images you want to mask with AutoMask by clicking the `Load input image(s)` button.

Select the preset you want to use with the dropdown in the top-right. The presets included by default are `Classic Kakariko Route (Double KO)` and `Classic Kakariko Route (Gohma Clip)` so far.

Select an input image in the dropdown underneath the left image (input preview), and select the corresponding mask to use for the image in the dropdown underneath the right image (output preview).

Set an output folder by clicking the `Set output` button.

Click `Save masked image` to save the current output to your selected output folder with the automatically generated filename shown above the output preview.

Press the arrows next to the dropdowns to move to the previous/next input and mask in the list. The splits in the presets are ordered by their order in their preset.json file. Input images are ordered alphabetically, so if you your screenshots in order using AutoSplit, they will also be in order when loaded by AutoMask due to how AutoSplit names its screenshots.

Continue until you've saved a masked image for every split (that you plan to use).

If you have the same number of input images loaded as there are splits in your currently selected preset, you'll also have the ability to press `Save all split images` to mask and save all split images to your output folder at once. This assumes that the order of your loaded input images is the order of the splits in your split preset. If they are not, this will not work correctly.

# Presets

Presets define the sequence of splits and their mask images, thresholds, timing, and other settings for a specific game and category. They are stored in the `presets/` folder next to the executable.

Click **Edit** next to the preset dropdown to open the preset editor, where you can create new presets, modify existing ones, and manage splits. See [PRESETS.md](PRESETS.md) for full documentation on the preset editor.
