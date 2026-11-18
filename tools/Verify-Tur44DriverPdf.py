"""Inspect the PDF produced by the native Windows print driver."""
import argparse
import hashlib
import math
import sys
import unicodedata
from pathlib import Path

from pypdf import PdfReader

EXPECTED = {
    "portrait-range": (210, 297, ["SAYFA 2 Çığ ve İğdır", "SAYFA 3 Ölçü, şüphe, ıslak"]),
    "landscape-current": (297, 210, ["SAYFA 3 Ölçü, şüphe, ıslak"]),
    "native-save-draft": (210, 297, ["TUR 45 Çığ ve İğdır. Ölçü, şüphe, ıslak."]),
    "native-overwrite-draft": (210, 297, ["TUR 46 YENİ TASLAK — önceki PDF ile aynı değildir."]),
}


def verify(path, scenario):
    width_mm, height_mm, text = EXPECTED[scenario]
    expected_size = (width_mm * 72 / 25.4, height_mm * 72 / 25.4)
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
        stream.seek(0)
        reader = PdfReader(stream, strict=True)
        if reader.is_encrypted or len(reader.pages) != len(text):
            raise ValueError("Unexpected PDF encryption or page count.")

        for index, (page, expected_text) in enumerate(zip(reader.pages, text), start=1):
            rotation = page.rotation % 360
            if rotation not in (0, 90, 180, 270):
                raise ValueError(f"Page {index} has invalid rotation {rotation}.")
            for box in (page.mediabox, page.cropbox):
                size = (float(box.width) * page.user_unit, float(box.height) * page.user_unit)
                if rotation in (90, 270):
                    size = size[::-1]
                if not all(math.isclose(a, b, rel_tol=0, abs_tol=1) for a, b in zip(size, expected_size)):
                    raise ValueError(f"Page {index} is {size} pt, expected A4 {expected_size} pt.")
            content = unicodedata.normalize("NFC", " ".join(page.extract_text().split()))
            if content != expected_text:
                raise ValueError(f"Page {index}: expected {expected_text!r}, got {content!r}.")
            print(f"{scenario} page {index}: {size[0]:.2f} x {size[1]:.2f} pt; {content}")
    print(f"{scenario} Windows driver PDF SHA-256 {digest}")
    round_number = {"native-save-draft": 45, "native-overwrite-draft": 47}.get(scenario, 44)
    print(f"1/1 Tur{round_number} {scenario} native Windows PDF output checks passed.")


if __name__ == "__main__":
    # The Windows runner may redirect stdout through a non-Turkish code page.
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pdf", type=Path)
    parser.add_argument("scenario", choices=EXPECTED)
    args = parser.parse_args()
    verify(args.pdf, args.scenario)
