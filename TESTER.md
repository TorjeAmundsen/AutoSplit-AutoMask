# Live Output Tester

<img width="680" height="488" alt="AutoMask_iiQJsQYfa6" src="https://github.com/user-attachments/assets/2ca21027-d362-4601-9bb0-6a22a35762b9" />


The live output tester lets you compare a reference image against a live video feed in real time. Open it from the main window via the **Live tester** button. The reference and the live video feed are both nearest-neighbor scaled down to 320x240 before they are compared using L2 Norm, because this is how AutoSplit does it. This aims to achieve complete parity with how AutoSplit handles its comparison, so you can accurately test your output images before putting them into AutoSplit.

Other comparison methods than L2 Norm are currently not supported, and will not be implemented until someone submits and issue about needing it.

## Reference image

The reference is automatically built from the current preset, split, and base image selected in the main window. It updates live as you change selections.

You can also load a custom PNG reference using the **Load custom image** button. While a custom image is loaded, live updates from the main window are paused. If the filename contains a threshold in parentheses (e.g. `split_001 (0.95).png`), it will be used as the required value.

## Feed source

Select a capture source from the dropdown, grouped by type: webcams, windows, and screen region. The feed displays at 320x240 regardless of source resolution, because this is how AutoSplit also behaves. Use the **Refresh** button to update the list if windows or devices have changed.

When selecting a window, the crop dimensions are automatically set to match the window's client area resolution.

The feed runs independently of the reference, it will display even if no reference image is loaded. Similarity comparison only begins once a reference is available.

## Crop

Adjust X, Y, Width, and Height to crop the capture source before it is scaled to 320x240. This is useful for isolating a specific region of a window or screen, or to crop out OBS Virtual Cam's black bars. **Reset crop** restores the crop to the full source dimensions.

## Similarity

- **Required**: the threshold defined in the preset split
- **Current**: the live similarity score between the reference and the feed
- **Highest**: the peak similarity seen since the last reset (reset with the arrow button)

When the current similarity meets or exceeds the required threshold, the current value turns green.

## Preferences

Capture source and crop settings are saved automatically when you close the window and confirm. They are restored the next time you open the tester for the same preset.
