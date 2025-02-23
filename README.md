## Download

 - Download the [latest version](/../../releases/latest)


# AutoMask for AutoSplit

This program uses preset split sequences and masks to automatically generate working AutoSplit images from your original screenshots. The generated images will be automatically named with adequate filenames, including threshold levels, delay times, dummy tags, etc. all pre-defined by the preset.

Quick and easy way to instantly set up AutoSplit, given your desired category has an AutoMask preset.

![AutoSplit-AutoMask_nLLqPqEMpg](https://github.com/user-attachments/assets/a179f3d1-c32d-4f34-b8a0-587b75e2499a)

# How to use
First, make sure the game feed you use for AutoSplit has *no black borders* in its output, your feed needs to be cropped correctly for this to work. It will adjust automatically if your aspect ratio is off, but it can't detect black bars around your feed and correct for them.

Select the images you want to mask with AutoMask by clicking the `Load input image(s)` button.

Select the preset you want to use with the dropdown in the top-right. The presets included by default are `Classic Kakariko Route (Double KO)` and `Classic Kakariko Route (Gohma Clip)` so far.

Select an input image in the dropdown underneath the left image (input preview), and select the corresponding mask to use for the image in the dropdown underneath the right image (output preview).

Set an output folder by clicking the `Set output folder` button.

Click `Save masked image` to save the current output to your selected output folder with the automatically generated filename shown above the output preview.

Press the arrows next to the dropdowns to move to the previous/next input and mask in the list. The splits in the presets are ordered chronologically, except for reset and start images, which come last. Input images are ordered alphabetically, so if you took them in order using AutoSplit, they will also be in order when loaded by AutoMask due to how AutoSplit names its screenshots.

Continue until you've saved a masked image for every split (that you plan to use).
