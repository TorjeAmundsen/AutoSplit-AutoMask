# Preset Editor

The preset editor lets you create and modify presets for AutoMask. Open it from the main window by clicking **Edit** next to the preset dropdown.

<img width="960" height="720" alt="AutoMask_p7z5dp7kOB" src="https://github.com/user-attachments/assets/3ea5e8fb-972a-44ec-b139-9a26b446031e" />

---

## The Preset List

The left panel shows all loaded presets, grouped alphabetically by game name.

- **Collapse/expand a group** - click the group header. Hovering over a header highlights the header and all presets in its group.
- **Dirty indicator** - an orange bar on the left edge of a preset means it has unsaved changes.
- **New Preset** - the button at the bottom creates a blank preset and selects it immediately.

---

## Preset Fields

At the top of the right panel, two fields apply to the whole preset:

| Field           | Description                                                                                                  |
| --------------- | ------------------------------------------------------------------------------------------------------------ |
| **Preset Name** | The display name. Also used to derive the folder name on disk (spaces become underscores). Required to save. |
| **Game Name**   | Groups the preset in the list. Typing shows suggestions from existing game names. Optional.                  |

---

## The Splits List

Below the preset fields is a list of the splits that make up the preset, shown as `N. SplitName`.

### Toolbar

| Button | Action                                                    | Enabled when        |
| ------ | --------------------------------------------------------- | ------------------- |
| **+**  | Insert a new split after the selected one (or at the end) | Preset is selected  |
| **⧉**  | Duplicate the selected split, inserting the copy below it | A split is selected |
| **−**  | Delete the selected split                                 | Preset is selected  |
| **↑**  | Move the selected split up                                | Preset is selected  |
| **↓**  | Move the selected split down                              | Preset is selected  |

All toolbar buttons are disabled when no preset is selected.

---

## Split Fields

Selecting a split opens its property form to the right.

### Name

The split's identifier. Used in the generated mask filename (see [Filename Preview](#filename-preview) below).

**Forbidden characters:** `#  @  {  }  (  )  [  ]  ^`

If any split has an invalid name its text box turns red and saving is disabled until it is fixed.

### Mask Image

The PNG mask image for this split. Click **Browse...** to pick a file. Only PNGs are accepted.

- The field shows the path relative to the preset folder if the file is inside it, or the full absolute path otherwise.
- The mask preview updates automatically when a file is selected.
- If the file cannot be found or loaded, the mask preview area shows **"Failed to load image"** in red.

### Threshold

Controls how closely the masked screenshot must match before AutoSplit triggers. Adjust with the text box or the slider.

- Range: `0.0` – `1.0`
- Default: `0.95`
- Uncheck the **Threshold** checkbox to use the AutoSplit default.

### Pause

Delay after this split triggers until it starts looking for the next split image, in seconds.

- Must be `>= 0`
- Default: `3.0 s`
- Uncheck the **Pause** checkbox to use the AutoSplit default.

### Delay

An additional delay before the split triggers, in milliseconds.

- Must be a non-negative integer
- Default: `0`
- Uncheck the **Delay** checkbox to omit it.

### Dummy

Marks this split image as a dummy split that will not trigger a split, but rather act as a prerequisite to the following image.

### Inverted

When checked, the trigger for the splits _inverts_. This means that instead of triggering the split when the image reaches its similarity threshold, it waits for similarity to drop _below_ its similarity threshold before it triggers.

---

## Filename Preview

The small grey text below the Name field shows the filename that will be given to this split's mask image when the preset is saved. It updates in real time as you edit the split's fields.

Examples:

| Settings        | Generated filename                            |
| --------------- | --------------------------------------------- |
| Name only       | `01_splitname.png`                            |
| With threshold  | `01_splitname_(0.95).png`                     |
| With pause time | `01_splitname_[3.0].png`                      |
| With delay      | `01_splitname_#100#.png`                      |
| Dummy           | `01_splitname_{d}.png`                        |
| Inverted        | `01_splitname_{b}.png`                        |
| Combined        | `01_splitname_(0.95)_[3.0]_#100#_{d}_{b}.png` |

The special split names `reset` and `start_auto_splitter` are not given a numeric prefix.

---

## Image Previews

### Mask Preview

Shows the selected mask image. Updates automatically when you switch splits or browse for a new mask. Displays **"Failed to load image"** in red if the file is missing or unreadable.

### Output Preview

Shows the result of applying the current mask to a base image - useful for checking that the mask looks correct before saving.

- **Load a base image** - click anywhere in the output preview area and pick a PNG.
- **Clear** - click the **Clear** button next to the "Output preview" label to unload the base image. The button is disabled when no image is loaded.

The output preview only renders when both a base image and a valid mask are loaded.

---

## Saving

### Save

Saves the currently selected preset. Enabled when the preset name is not empty and no split has invalid characters.

- If the preset was loaded from disk, it is saved back to the same folder.
- If it is a new preset that has never been saved, the **Save As New Preset** flow runs instead.

### Save All

Saves every preset that has unsaved changes, not just the selected one. Enabled when there are dirty presets other than the currently selected one.

### Save As New Preset

Saves the current preset to a new folder. Enabled under the same conditions as **Save**.

1. The folder name is derived from the preset name.
2. If a folder with that name already exists, a prompt asks for a different name.
3. If the renamed folder also exists, a confirmation asks whether to overwrite it.
4. Any mask files outside the preset folder are copied into it.

After saving, the preset list rebuilds so the preset appears under the correct game name group.

---

## Unsaved Changes

### Switching presets

If the current preset has unsaved changes when you click a different preset, a dialog appears:

| Choice     | Effect                                                                                                                         |
| ---------- | ------------------------------------------------------------------------------------------------------------------------------ |
| **Yes**    | Saves the current preset, then switches. If the save requires a rename prompt and you cancel it, the switch is also cancelled. |
| **No**     | Discards changes and switches immediately.                                                                                     |
| **Cancel** | Returns to the current preset.                                                                                                 |

### Closing the editor

If any presets have unsaved changes when you click **Close** (or close the window), a dialog lists them and asks whether to close and discard. Choosing **No** returns you to the editor.

---

## Disk Layout

Presets are stored in the `presets/` directory, along with all mask images used by the preset. Each preset gets its own subfolder:

```
presets/
  My_Preset/
    preset.json
    some_mask.png
    another_mask.png
    ...
```

The folder name is the preset name with spaces replaced by underscores and any filesystem-invalid characters removed.

### preset.json

```json
{
    "$schema": "../preset-schema.json",
    "presetName": "My Preset",
    "gameName": "My Game",
    "splits": [
        {
            "mask": "some_mask.png",
            "name": "First Split",
            "threshold": 0.95
        },
        {
            "mask": "another_mask.png",
            "name": "Second Split",
            "threshold": 0.95,
            "pauseTime": 3.0,
            "inverted": true
        }
    ]
}
```

Fields are only written when they differ from the AutoSplit default:

| Field       | Omitted when                        |
| ----------- | ----------------------------------- |
| `gameName`  | Empty                               |
| `threshold` | Threshold checkbox is unchecked     |
| `pauseTime` | Pause checkbox is unchecked         |
| `delay`     | Delay is 0 or checkbox is unchecked |
| `dummy`     | `false`                             |
| `inverted`  | `false`                             |
