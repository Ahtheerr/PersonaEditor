from __future__ import annotations

import re
import struct
import sys
import unittest
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path


SHOP_DIR = Path(__file__).resolve().parent
ROMFS_ROOT = SHOP_DIR.parent
sys.path.insert(0, str(SHOP_DIR))

import mbm_decode  # noqa: E402


CONTROL_TOKEN = re.compile(r"(\{(?:F8|80)[0-9A-F]{2}(?:,[^}]*)?\}|\\0)")

EXPECTED_CONTROL_COUNTS = {
    "F801": 81616,
    "F802": 25545,
    "F804": 4760,
    "F811": 102,
    "F812": 10939,
    "F813": 1358,
    "F814": 57,
    "F81B": 11600,
    "F842": 2,
    "F843": 913,
    "F871": 13962,
    "F872": 843,
    "F873": 376,
    "F874": 2,
    "F875": 136,
    "F877": 224,
    "F878": 578,
    "F87A": 8880,
    "F87B": 1689,
    "F87D": 3449,
    "F87E": 55,
    "8001": 262,
    "8002": 76,
    "8004": 84,
    "8012": 83,
    "8013": 5,
    "8016": 42,
    "8017": 42,
    "8018": 42,
    "8041": 1,
    "8042": 1,
    "8043": 4,
    "8051": 1,
    "8052": 1,
    "8053": 1,
    "8054": 1,
    "8055": 1,
    "8056": 1,
    "8057": 1,
    "8058": 1,
    "807A": 2,
}


def encode_raw_decoded_record(text: str) -> bytes:
    output = bytearray()
    for part in CONTROL_TOKEN.split(text):
        if not part:
            continue
        if part == r"\0":
            output.extend(b"\0\0")
            continue
        if not part.startswith("{"):
            output.extend(part.encode("cp932"))
            continue

        fields = part[1:-1].split(",")
        code = fields[0]
        introducer = int(code[:2], 16)
        opcode = int(code[2:], 16)
        args = [int(value) for value in fields[1:]]
        output.extend((introducer, opcode))
        spec = mbm_decode.CONTROL_ARGS[introducer][opcode]
        if spec == ("f81b",):
            output.extend(struct.pack("<III", *args))
            continue
        for arg_type, value in zip(spec, args, strict=True):
            output.extend(struct.pack("<H" if arg_type == "u16" else "<I", value))
    output.extend(b"\xFF\xFF")
    return bytes(output)


class MbmDecoderTests(unittest.TestCase):
    def test_every_mbm_in_dump(self) -> None:
        files = mbm_decode.expand_input_paths([str(ROMFS_ROOT)])
        totals = {"entries": 0, "slots": 0, "text_bytes": 0, "empty": 0, "dense": 0}
        control_counts: Counter[str] = Counter()

        for path in files:
            with self.subTest(path=path.relative_to(ROMFS_ROOT)):
                header, entries = mbm_decode.parse_mbm(path)
                ET.fromstring(mbm_decode.entries_to_xml(entries))
                data = path.read_bytes()
                raw_header, raw_entries = mbm_decode.parse_mbm(
                    path, normalize=False, legacy_f81b=True
                )
                self.assertEqual(raw_header, header)
                for entry in raw_entries:
                    original = data[entry.offset : entry.offset + entry.byte_length]
                    self.assertEqual(encode_raw_decoded_record(entry.text), original)
                    control_counts.update(
                        match.group(0)[1:5]
                        for match in CONTROL_TOKEN.finditer(entry.text)
                        if match.group(0).startswith("{")
                    )
                totals["entries"] += len(entries)
                totals["slots"] += header.slot_count
                totals["text_bytes"] += header.text_blob_size
                totals["empty"] += not entries
                totals["dense"] += header.used_size == len(data)

        self.assertEqual(len(files), 1246)
        self.assertEqual(
            totals,
            {
                "entries": 37158,
                "slots": 467075,
                "text_bytes": 4899518,
                "empty": 28,
                "dense": 612,
            },
        )
        self.assertEqual(dict(control_counts), EXPECTED_CONTROL_COUNTS)

    def test_all_observed_control_layout_shapes(self) -> None:
        record = bytes.fromhex(
            "F8 13 0A 00 00 00 01 00 00 00 "
            "F8 14 01 00 02 00 "
            "F8 7B AE 00 03 00 01 00 01 00 "
            "F8 7E "
            "80 13 02 00 "
            "80 16 93 C5 80 17 82 C7 82 AD 80 18 "
            "FF FF"
        )
        self.assertEqual(
            mbm_decode.decode_record(record, normalize=False),
            "{F813,10,1}{F814,1,2}{F87B,174,3,1,1}{F87E}"
            "{8013,2}{8016}\u6BD2{8017}\u3069\u304F{8018}",
        )

    def test_legacy_sidecar_compatibility(self) -> None:
        path = SHOP_DIR / "shop_ginzaitm.mbm"
        _, entries = mbm_decode.parse_mbm(path, legacy_f81b=True)
        actual = mbm_decode.entries_to_xml(entries).rstrip("\n")
        expected = (SHOP_DIR / "shop_ginzaitm.mbm.xml").read_text(
            encoding="utf-8"
        ).replace("\r\n", "\n").rstrip("\n")
        self.assertEqual(actual, expected)


if __name__ == "__main__":
    unittest.main()
