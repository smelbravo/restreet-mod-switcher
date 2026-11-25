"""Create a data-only ReStreet archive patch for a Thunderstore package."""

import argparse
import hashlib
import struct
from pathlib import Path
from tempfile import NamedTemporaryFile


BLOCK_SIZE = 64 * 1024
MAX_FILE_SIZE = (1 << 31) - 1


def same_edges(old, new):
    limit = min(len(old), len(new))
    prefix = 0
    while prefix < limit and old[prefix] == new[prefix]:
        prefix += 1
    suffix = 0
    # A copy command still needs eight bytes, so short suffixes are not worth encoding.
    if len(old) == len(new):
        while suffix < limit - prefix and old[-suffix - 1] == new[-suffix - 1]:
            suffix += 1
    return prefix if prefix >= 8 else 0, suffix if suffix >= 8 else 0


def write_patch(original, modified, patch):
    with original.open("rb") as base, modified.open("rb") as target:
        source_length = original.stat().st_size
        target_length = modified.stat().st_size
        if not 0 < source_length <= MAX_FILE_SIZE or not 0 < target_length <= MAX_FILE_SIZE:
            raise ValueError("Each archive must be between 1 byte and 2 GiB.")
        source_hash = hashlib.file_digest(base, "sha256").digest()
        base.seek(0)
        target_hash = hashlib.sha256()
        patch.write(b"RSP1" + struct.pack("<i", target_length) + bytes(32) + struct.pack("<i", 1))
        patch.write(source_hash + bytes(4))
        recipe_start = patch.tell()
        position = 0
        while new := target.read(BLOCK_SIZE):
            old = base.read(len(new))
            target_hash.update(new)
            if old == new:
                patch.write(struct.pack("<ii", position, len(new)))
            else:
                prefix, suffix = same_edges(old, new)
                if prefix:
                    patch.write(struct.pack("<ii", position, prefix))
                changed = new[prefix:len(new) - suffix]
                if changed:
                    patch.write(struct.pack("<ii", -1, len(changed)))
                    patch.write(changed)
                if suffix:
                    patch.write(struct.pack("<ii", position + len(new) - suffix, suffix))
            position += len(new)
        recipe_size = patch.tell() - recipe_start
        if position != target_length or recipe_size > MAX_FILE_SIZE:
            raise ValueError("The modified archive changed during the build or the patch is too large.")
        patch.seek(8)
        patch.write(target_hash.digest())
        patch.seek(76)
        patch.write(struct.pack("<i", recipe_size))
        return recipe_size


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("original", type=Path, help="Original archive from a clean Vanilla installation.")
    parser.add_argument("modified", type=Path, help="The corresponding archive with the mod installed.")
    parser.add_argument("output", type=Path, help="Patch path inside the package's restreet-patches folder.")
    arguments = parser.parse_args()
    output = arguments.output.resolve()
    if output.exists():
        parser.error(f"The output already exists: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    with NamedTemporaryFile(dir=output.parent, delete=False) as destination:
        temporary = Path(destination.name)
        try:
            size = write_patch(arguments.original, arguments.modified, destination)
            destination.close()
            temporary.rename(output)
        finally:
            temporary.unlink(missing_ok=True)
    print(f"{output} ({size + 80:,} bytes)")


if __name__ == "__main__":
    main()
