# RunLeash Export

AutoMask can export the currently loaded preset and base images as a [RunLeash](https://autosplithelper-firebase.web.app/) profile. RunLeash is a Japanese AutoSplit alternative; it consumes a `table.csv` plus a `picture/` folder of raw game frames instead of AutoSplit's named PNG convention.

## How to Use

1. Select a preset from the dropdown.
2. Click **Load base image(s)** and pick screenshots that match the preset splits in order. The number of base images must equal the number of splits.
3. Click **Export RunLeash** at the bottom right.
4. Pick a destination folder. AutoMask writes:
   - `table.csv` - one row per split (plus a reserved Reset row at the top)
   - `picture/N.png` - the base image for split `N`, resized to that split's mask resolution

Open the destination folder as a profile in RunLeash.

## What Gets Exported

| AutoMask field        | RunLeash CSV column            | Notes                                                              |
| --------------------- | ------------------------------ | ------------------------------------------------------------------ |
| Split name            | `Comment`                      | as-is                                                              |
| Threshold (`0.0-1.0`) | `閾値` (Threshold)             | `round(threshold * 100)`                                           |
| Pause time (s)        | `待機時間` (Wait time)         | rounded to integer seconds                                         |
| Delay (ms)            | `T` + `遅延時間`               | `T=1` if delay > 0, otherwise `T=0`                                |
| Inverted              | `PN`                           | `n` if inverted, else `p`                                          |
| Dummy                 | `Key`                          | `-1` if dummy (no key sent), else `0` (Split key)                  |
| Mask alpha bbox       | `PX`, `PY`, `SX`, `SY`         | bounding box of opaque pixels in the mask, in mask pixel space     |

## Special Splits

- A split named `reset` fills the reserved Reset row at the top of the CSV. No `picture/N.png` is written for it.
- A split named `start_auto_splitter` is moved to the first row after Reset so it triggers first. It's exported as a regular split (`Key=0`).
- Any other split keeps its order from the preset.

## Defaults Applied to Every Row

- Algorithm: **L2 Norm** (`方式=8`) - the same algorithm AutoMask and AutoSplit use.
- `Loop=1`, `And=0`, `AdjTable=0`, `AdjTimer=0`, `Seek=-1`, `M=0`, `A=0`
- `閾値N=90`, `R=G=B=0`, `色許容=10`, `Filter=none`, `Akaze=300`
- Scene-change rect (`PX(S)/PY(S)/SX(S)/SY(S)`) defaults to a 40x40 box centered on the picture
- Limited-search rect set to no-limit (`-999999` / `999999`)

If you need any of these tweaked per-split, edit `table.csv` after export.

## Limitations

- L2 Norm only. Other RunLeash matching methods (Template, AKAZE, ORB, POC, hashes, RGB color matching) are not exposed.
- Filters are not exposed (`Filter=none` always).
- Audio matching (`A=1`) is not exposed.
- The export is one-way: there is no RunLeash -> AutoMask import.
- Base image dimensions are normalized to the mask resolution; if your base image aspect ratio differs from the mask, it will be stretched.
