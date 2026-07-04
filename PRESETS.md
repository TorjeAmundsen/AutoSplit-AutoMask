# Preset Editor

The preset editor lets you create and modify presets for AutoMask. Open it from the main window by clicking **Edit** next to the preset dropdown.

<img width="960" height="720" alt="AutoMask_ck0cqZTT6u" src="https://github.com/user-attachments/assets/d0795071-fd3a-4abb-bdec-93da10235924" />


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
| **−**  | Delete the selected split                                 | Preset is selected  |
| **⧉**  | Duplicate the selected split, inserting the copy below it | A split is selected |
| **📥** | Import pre-made splits (see [below](#pre-made-splits))    | Preset is selected  |

All toolbar buttons are disabled when no preset is selected.

### Reordering

Splits can be reordered by dragging them up or down in the list.

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

### Savestate

An optional savestate file associated with this split to aid users in taking screenshots. This is generally designed for Ocarina of Time gz savestates, but it technically supports any arbitrary files. Click **Browse...** to pick a file of any type, or **Clear** to remove the association.

- Any file extension is allowed - the format is not validated.
- The field shows the path relative to the preset folder if the file lives inside it, or the full absolute path otherwise.
- On save, files outside the preset folder are copied into a `savestates/` subfolder next to `preset.json`.
- When a preset is saved, any file in `savestates/` that is no longer referenced by a split is deleted. Deleting a split (or clearing its savestate) and saving prunes the associated file, unless another split still references it.

Savestates are exposed in the main window through the **Copy savestates** button, which copies all referenced files to the clipboard with the naming pattern `{index}_{split name}.{ext}`.

### Savestate Instructions

A free-form text field that appears only when the split has a savestate assigned. Use it for notes about how the savestate is meant to be used - pre-screenshot setup requirements, menu setup, timing caveats, etc.

- Saved to `preset.json` as `savestateInstructions` (only when a savestate is also set and the text is non-empty).
- Clearing the savestate also clears the associated instructions.
- Accessible from the main window via the **Instructions** button next to **Copy savestates**, which opens a window listing every split's instructions in order. The button is enabled only when at least one split in the current preset has non-empty instructions.

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

## Pre-made Splits

The **📥** toolbar button opens a dialog where you can import common, ready-to-use splits into the currently selected preset. Pre-made splits come bundled with the application and include mask images and recommended properties already configured.

In the import dialog, splits are grouped by game and, optionally, by **section** within a game. Each section header can be clicked to collapse or expand it. Each split is shown as a card with its name, a short description, property summary (threshold, pause, delay, etc.), and a thumbnail of the recommended base image when available. Check the splits you want and click **Import Selected** - they are inserted after the currently selected split in the preset. Collapsing a section does not clear its checked splits.

Mask images are copied into the preset folder when you save.

### Adding a New Pre-made Split

The **Add New…** button in the import dialog opens an editor for creating a brand-new pre-made split. Pick (or type) a game and a section, give the split a name, set its properties (threshold, pause, delay, dummy, inverted), choose a **mask** PNG, and optionally choose a **base image**. On save:

- The mask is copied into the game's `masks/` folder (keeping its source filename so the same mask can be reused by other splits).
- The base image is **automatically scaled down to a 64×48 thumbnail** and saved into the game's `thumbnails/` folder, named after the split (lowercased, spaces → underscores, with a `_thumb` suffix — e.g. *Enter Deku* → `enter_deku_thumb.png`).
- The split is appended to the section's `splits.json` (a new section gets the next numeric prefix). The import list refreshes so the new split appears immediately.

### Pre-made Splits Disk Layout

Pre-made splits live in the bundled `splits/` directory, one folder per game. Each game keeps its full-resolution mask images in a shared `masks/` folder and its 64×48 base-image thumbnails in a shared `thumbnails/` folder, both under the game directory, so the same image can be reused by multiple splits or sections without being duplicated on disk. A game folder can hold splits in either layout:

```
splits/
  OcarinaOfTime/
    masks/                     # full-res mask PNGs for this game
      reset.png  start.png  escape.png  ...
    thumbnails/                # 64x48 base images
      base_links_house.png  base_ganon.png  ...
    01 Start, Reset, End/      # a section
      splits.json              # mask -> ../masks/<file>, baseImage -> ../thumbnails/<file>
    02 Transitions/            # another section
      splits.json
```

or, with no sections, a single `splits.json` directly inside the game folder (referencing `masks/<file>` and `thumbnails/<file>`).

- The **game name** comes from each `splits.json`'s `gameName`; all sections of a game share it.
- A **section's display name** is its subfolder name. A leading number orders the sections in the dialog and is stripped from the displayed name (e.g. `01 Start, Reset, End` shows as **Start, Reset, End**). Sections without a number prefix sort last, alphabetically.
- Mask and base-image paths in each `splits.json` are relative to the folder that contains it, so a section references the shared images as `../masks/<file>` and `../thumbnails/<file>`.

---

## Disk Layout

Presets are stored in the `presets/` directory, along with all mask images used by the preset. Each preset gets its own subfolder:

```
presets/
  My_Preset/
    preset.json
    some_mask.png
    another_mask.png
    savestates/
      split1.gzs
      split2.gzs
    ...
```

The `savestates/` subfolder is only created if at least one split in the preset has a savestate.

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
| `savestate` | No savestate is assigned            |
| `savestateInstructions` | Empty, or no savestate is assigned |
