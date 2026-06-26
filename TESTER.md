# Live Output Tester

<img width="680" height="588" alt="AutoMask_uYIjrn8ccr" src="https://github.com/user-attachments/assets/550ffd0e-4f75-4fb4-bd26-d6731d24160b" />


The live output tester lets you compare a reference image against a live video feed in real time. Open it from the main window via the **Live tester** button. The reference and the live video feed are both nearest-neighbor scaled down to 320x240 before they are compared using L2 Norm, because this is how AutoSplit does it. This aims to achieve complete parity with how AutoSplit handles its comparison, so you can accurately test your output images before putting them into AutoSplit.

Other comparison methods than L2 Norm are currently not supported, and will not be implemented until someone submits and issue about needing it.

## Reference image

The reference is automatically built from the current preset, split, and base image selected in the main window. It updates live as you change selections.

You can also load a custom PNG reference using the **Load custom image** button. While a custom image is loaded, live updates from the main window are paused. The threshold, inverted flag, and delay are read from the filename using AutoSplit's naming conventions.

References built from a preset split instead take their threshold, inverted flag, and delay directly from the split definition.

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

The Required, Current, and Highest values are rendered in a monospace font so the digits stay aligned and don't flicker as the score updates.

## Matched frame

The panel under the reference freezes the exact frame that trips the split, so you can see what the feed looked like at the trigger point. This mirrors how AutoSplit decides to split:

- **Normal splits**: the first frame whose similarity reaches the threshold is captured ("Matched frame").
- **Inverted splits**: the initial match is captured, and then the first frame that drops back below the threshold again is also captured, since this is the frame AutoSplit splits on for inverted images. Without a delay this frame is the "Split frame"; with a delay it is the "Trigger frame" and the actual "Split frame" is captured once the delay elapses.

### Use delay

Check **Use delay** to also capture an extra frame at the split's delay time. After a trigger, the panel keeps the trigger frame and then captures another frame once the delay has elapsed, labeled with the delay (e.g. `Matched frame +1000 ms`). For inverted splits, the delay applies after the drop below the threshold only. The delay value comes from the preset split, or from a custom image's filename. With no delay set, the toggle has no effect.

### Browsing captured frames

When more than one frame has been captured, use the **◀** and **▶** arrows beside the caption to scroll through them. The caption shows the position (e.g. `(2/3)`). The ↺ button next to the similarity values clears the captured frames and re-arms capture.

## Preferences

Capture source and crop settings are saved automatically when you close the window and confirm. They are restored the next time you open the tester for the same preset.
