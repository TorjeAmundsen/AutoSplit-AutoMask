# this just resizes the base image thumbs for pre-made splits to 64x48
# 64x48 is the res they're displayed at in the gui so they don't need to be bigger than that

import os
import sys

from PIL import Image


def resize_images(directory):
    for filename in os.listdir(directory):
        if filename.startswith("base_") and filename.endswith(".png"):
            filepath = os.path.join(directory, filename)

            try:
                with Image.open(filepath) as img:
                    resized = img.resize((64, 48), Image.Resampling.LANCZOS)
                    resized.save(filepath)  # overwrites original, careful
                    print(f"Resized: {filename}")
            except Exception as e:
                print(f"Failed on {filename}: {e}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("Usage: python script.py <directory_path>")
        sys.exit(1)

    folder_path = sys.argv[1]

    if not os.path.isdir(folder_path):
        print("Error: Provided path is not a valid directory.")
        sys.exit(1)

    resize_images(folder_path)
