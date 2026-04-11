## Download

 - Download the [latest version](/../../releases/latest)


# AutoMask for AutoSplit

This program uses preset split sequences and masks to automatically generate working AutoSplit images from your original screenshots. The generated images will be automatically named with adequate filenames, including threshold levels, delay times, dummy tags, etc. all pre-defined by the preset.

Quick and easy way to instantly set up AutoSplit, given your desired category has an AutoMask preset.

<img width="832" height="499" alt="image" src="https://github.com/user-attachments/assets/20f3feb4-ebd7-48f0-9fc4-05763aa28382" />

# How to use
First, make sure the game feed you use for AutoSplit has *no black borders* in its output, your feed needs to be cropped correctly for this to work. It will adjust automatically if your aspect ratio is off, but it can't detect black bars around your feed and correct for them, nor will it correct for your feed being over-cropped.

Select the images you want to mask with AutoMask by clicking the `Load input image(s)` button.

Select the preset you want to use with the dropdown in the top-right. The presets included by default are `Classic Kakariko Route (Double KO)` and `Classic Kakariko Route (Gohma Clip)` so far.

Select an input image in the dropdown underneath the left image (input preview), and select the corresponding mask to use for the image in the dropdown underneath the right image (output preview).

Set an output folder by clicking the `Set output` button.

Click `Save masked image` to save the current output to your selected output folder with the automatically generated filename shown above the output preview.

Press the arrows next to the dropdowns to move to the previous/next input and mask in the list. The splits in the presets are ordered by their order in their preset.json file. Input images are ordered alphabetically, so if you your screenshots in order using AutoSplit, they will also be in order when loaded by AutoMask due to how AutoSplit names its screenshots.

Continue until you've saved a masked image for every split (that you plan to use).

If you have the same number of input images loaded as there are splits in your currently selected preset, you'll also have the ability to press `Save all split images` to mask and save all split images to your output folder at once. This assumes that the order of your loaded input images is the order of the splits in your split preset. If they are not, this will not work correctly.
