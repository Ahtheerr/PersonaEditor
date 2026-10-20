#!/usr/bin/env python3
"""Decode MSG2/MBM message files to the XML sidecar format used here."""

from __future__ import annotations

import argparse
import glob
import os
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable
from xml.sax.saxutils import escape


HEADER_SIZE = 0x20
ROW_SIZE = 0x10
MAGIC = b"MSG2"
VERSION = 0x00010000

# Argument layouts observed across every MBM in this dump. Integers are little-endian.
# The alternate 0x80 introducer has its own opcode layouts; notably, 0x13 differs.
CONTROL_ARGS: dict[int, dict[int, tuple[str, ...]]] = {
    0xF8: {
        0x01: (),
        0x02: (),
        0x04: ("u16",),
        0x11: ("u16",),
        0x12: (),
        0x13: ("u32", "u32"),
        0x14: ("u16", "u16"),
        0x1B: ("f81b",),
        0x42: ("u16",),
        0x43: ("u16",),
        0x71: ("u16",),
        0x72: ("u16",),
        0x73: ("u16",),
        0x74: ("u16",),
        0x75: ("u16",),
        0x77: ("u16",),
        0x78: ("u16",),
        0x7A: ("u16",),
        0x7B: ("u16", "u16", "u16", "u16"),
        0x7D: ("u16",),
        0x7E: (),
    },
    0x80: {
        0x01: (),
        0x02: (),
        0x04: ("u16",),
        0x12: (),
        0x13: ("u16",),
        0x16: (),
        0x17: (),
        0x18: (),
        0x41: ("u16",),
        0x42: ("u16",),
        0x43: ("u16",),
        0x51: ("u16",),
        0x52: ("u16",),
        0x53: ("u16",),
        0x54: ("u16",),
        0x55: ("u16",),
        0x56: ("u16",),
        0x57: ("u16",),
        0x58: ("u16",),
        0x7A: ("u16",),
    },
}

SPECIAL_NORMALIZATION = str.maketrans(
    {
        "\u3000": " ",
        "\u2018": "'",
        "\u2019": "'",
        "\u201c": '"',
        "\u201d": '"',
        "\u2026": "...",
        "\uFFE5": "\u00A5",
    }
)


class MbmError(ValueError):
    pass


@dataclass(frozen=True)
class Header:
    used_size: int
    entry_count: int
    table_offset: int
    text_start: int
    text_blob_size: int
    slot_count: int


@dataclass(frozen=True)
class Entry:
    msg_id: int
    byte_length: int
    offset: int
    unknown: int
    text: str


def u32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def parse_header(data: bytes, path: Path) -> Header:
    if len(data) < HEADER_SIZE:
        raise MbmError(f"{path}: file is too small for an MBM header")
    if data[0:4] != b"\0\0\0\0":
        raise MbmError(f"{path}: expected zero prefix at 0x00")
    if data[4:8] != MAGIC:
        raise MbmError(f"{path}: expected MSG2 magic at 0x04")
    version = u32(data, 0x08)
    if version != VERSION:
        raise MbmError(f"{path}: unsupported MSG2 version 0x{version:08X}")

    used_size = u32(data, 0x0C)
    entry_count = u32(data, 0x10)
    table_offset = u32(data, 0x14)
    if table_offset != HEADER_SIZE:
        raise MbmError(f"{path}: unsupported table offset 0x{table_offset:X}")
    if data[0x18:0x20] != b"\0" * 8:
        raise MbmError(f"{path}: expected zero padding at 0x18")

    text_blob_size = used_size - HEADER_SIZE - entry_count * ROW_SIZE
    if text_blob_size < 0:
        raise MbmError(f"{path}: invalid used size 0x{used_size:X}")
    if used_size > len(data):
        raise MbmError(
            f"{path}: logical used size 0x{used_size:X} exceeds file size 0x{len(data):X}"
        )
    text_start = len(data) - text_blob_size
    table_size = text_start - table_offset
    if table_size < 0 or table_size % ROW_SIZE:
        raise MbmError(f"{path}: invalid sparse table size 0x{table_size:X}")

    return Header(
        used_size=used_size,
        entry_count=entry_count,
        table_offset=table_offset,
        text_start=text_start,
        text_blob_size=text_blob_size,
        slot_count=table_size // ROW_SIZE,
    )


def normalize_cp932(text: str) -> str:
    # Avoid broad NFKC changes to Japanese text while retaining the established
    # sidecar convention for full-width English text and punctuation.
    normalized = "".join(
        chr(ord(char) - 0xFEE0) if "\uFF01" <= char <= "\uFF5E" else char
        for char in text
    )
    return normalized.translate(SPECIAL_NORMALIZATION)


def read_control_args(
    record: bytes, offset: int, limit: int, spec: tuple[str, ...]
) -> tuple[list[int], int]:
    args: list[int] = []
    pos = offset
    for arg_type in spec:
        if arg_type == "u16":
            if pos + 2 > limit:
                raise MbmError("truncated u16 control argument")
            args.append(struct.unpack_from("<H", record, pos)[0])
            pos += 2
        elif arg_type == "u32":
            if pos + 4 > limit:
                raise MbmError("truncated u32 control argument")
            args.append(struct.unpack_from("<I", record, pos)[0])
            pos += 4
        else:
            raise AssertionError(f"unknown arg type {arg_type}")
    return args, pos


def decode_f81b(
    record: bytes, offset: int, limit: int, *, legacy_f81b: bool
) -> tuple[str, int]:
    if offset + 12 > limit:
        raise MbmError("truncated F81B payload")
    payload = record[offset : offset + 12]
    ident_raw = payload[:8]
    tail = struct.unpack_from("<I", payload, 8)[0]
    if any(byte != 0 and not 32 <= byte < 127 for byte in ident_raw):
        raise MbmError("F81B identifier is not printable ASCII")
    nul = ident_raw.find(b"\0")
    if nul >= 0 and any(ident_raw[nul + 1 :]):
        raise MbmError("F81B identifier has nonzero bytes after its NUL padding")
    ident = ident_raw[:nul if nul >= 0 else len(ident_raw)].decode("ascii")
    if not ident:
        raise MbmError("F81B identifier is empty")
    if legacy_f81b:
        args = struct.unpack_from("<III", payload)
        return "{F81B,%d,%d,%d}" % args, offset + 12
    if tail:
        return f"{{F81B,{ident},{tail}}}", offset + 12
    return f"{{F81B,{ident}}}", offset + 12


def decode_record(record: bytes, *, normalize: bool = True, legacy_f81b: bool = False) -> str:
    if not record.endswith(b"\xFF\xFF"):
        raise MbmError("message record does not end with FF FF")

    out: list[str] = []
    pos = 0
    content_end = len(record) - 2
    while pos < content_end:
        two = record[pos : pos + 2]
        if two == b"\xFF\xFF":
            raise MbmError("record terminator appears before the end of the record")
        if two == b"\0\0":
            out.append(r"\0")
            pos += 2
            continue
        if record[pos] in CONTROL_ARGS:
            if pos + 2 > content_end:
                raise MbmError("truncated control opcode")
            introducer = record[pos]
            opcode = record[pos + 1]
            spec = CONTROL_ARGS[introducer].get(opcode)
            code = f"{introducer:02X}{opcode:02X}"
            if spec is None:
                raise MbmError(f"unknown control opcode {code}")
            if spec == ("f81b",):
                token, pos = decode_f81b(
                    record, pos + 2, content_end, legacy_f81b=legacy_f81b
                )
                out.append(token)
                continue
            args, pos = read_control_args(record, pos + 2, content_end, spec)
            if args:
                out.append("{%s,%s}" % (code, ",".join(str(arg) for arg in args)))
            else:
                out.append(f"{{{code}}}")
            continue

        start = pos
        while pos < content_end:
            two = record[pos : pos + 2]
            if two in (b"\xFF\xFF", b"\0\0") or record[pos] in CONTROL_ARGS:
                break
            byte = record[pos]
            if byte == 0:
                raise MbmError(f"unexpected single NUL byte at record offset 0x{pos:X}")
            if byte < 0x80 or 0xA1 <= byte <= 0xDF:
                pos += 1
            elif 0x81 <= byte <= 0x9F or 0xE0 <= byte <= 0xFC:
                if pos + 2 > content_end:
                    raise MbmError(f"truncated CP932 character at record offset 0x{pos:X}")
                pos += 2
            else:
                raise MbmError(f"invalid CP932 lead byte 0x{byte:02X} at record offset 0x{pos:X}")
        try:
            decoded = record[start:pos].decode("cp932")
        except UnicodeDecodeError as exc:
            error_offset = start + exc.start
            raise MbmError(f"invalid CP932 sequence at record offset 0x{error_offset:X}") from exc
        out.append(normalize_cp932(decoded) if normalize else decoded)

    return "".join(out)


def parse_mbm(
    path: Path, *, normalize: bool = True, legacy_f81b: bool = False
) -> tuple[Header, list[Entry]]:
    data = path.read_bytes()
    header = parse_header(data, path)
    entries: list[Entry] = []
    record_cursor = header.text_start

    for slot in range(header.slot_count):
        row_offset = header.table_offset + slot * ROW_SIZE
        msg_id, byte_length, text_offset, unknown = struct.unpack_from("<IIII", data, row_offset)
        if (msg_id, byte_length, text_offset, unknown) == (0, 0, 0, 0):
            continue
        if msg_id != slot:
            raise MbmError(f"{path}: row at slot {slot} has id {msg_id}")
        if byte_length < 2:
            raise MbmError(f"{path}: entry {msg_id} is too short for a terminator")
        if text_offset < header.text_start or text_offset + byte_length > len(data):
            raise MbmError(f"{path}: entry {msg_id} points outside the text blob")
        if unknown != 0:
            raise MbmError(f"{path}: entry {msg_id} has nonzero row field 3: {unknown}")
        if text_offset != record_cursor:
            relation = "overlaps" if text_offset < record_cursor else "leaves a gap before"
            raise MbmError(
                f"{path}: entry {msg_id} {relation} its record at 0x{text_offset:X}; "
                f"expected 0x{record_cursor:X}"
            )
        try:
            text = decode_record(
                data[text_offset : text_offset + byte_length],
                normalize=normalize,
                legacy_f81b=legacy_f81b,
            )
        except MbmError as exc:
            raise MbmError(f"{path}: entry {msg_id}: {exc}") from exc
        entries.append(Entry(msg_id, byte_length, text_offset, unknown, text))
        record_cursor = text_offset + byte_length

    if len(entries) != header.entry_count:
        raise MbmError(f"{path}: header says {header.entry_count} entries, found {len(entries)}")
    if record_cursor != len(data):
        raise MbmError(
            f"{path}: text records end at 0x{record_cursor:X}, not file end 0x{len(data):X}"
        )

    return header, entries


def entries_to_xml(entries: Iterable[Entry]) -> str:
    lines = ["<mbm>"]
    for entry in entries:
        text = escape(entry.text)
        lines.extend(
            [
                f'  <entry id="{entry.msg_id}">',
                f"    <source>{text}</source>",
                f"    <target>{text}</target>",
                "  </entry>",
            ]
        )
    lines.append("</mbm>")
    return "\n".join(lines)


def write_sidecar(
    path: Path, xml: str, out_dir: Path | None, input_root: Path | None = None
) -> Path:
    if out_dir:
        relative = path.resolve().relative_to(input_root) if input_root else Path(path.name)
        output_path = out_dir / relative.parent / f"{relative.name}.xml"
    else:
        output_path = path.with_name(f"{path.name}.xml")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(xml, encoding="utf-8", newline="\n")
    return output_path


def write_stdout_utf8(text: str) -> None:
    sys.stdout.buffer.write(text.encode("utf-8"))


def expand_input_paths(patterns: Iterable[str]) -> list[Path]:
    paths: list[Path] = []
    for pattern in patterns:
        if any(char in pattern for char in "*?["):
            matches = sorted(glob.glob(pattern, recursive=True))
            if matches:
                candidates = [Path(match) for match in matches]
            else:
                raise MbmError(f"no files match input pattern: {pattern}")
        else:
            candidates = [Path(pattern)]
        for candidate in candidates:
            if candidate.is_dir():
                paths.extend(
                    path
                    for path in sorted(candidate.rglob("*"))
                    if path.is_file() and path.suffix.lower() == ".mbm"
                )
                continue
            paths.append(candidate)

    unique: list[Path] = []
    seen: set[str] = set()
    for path in paths:
        key = os.path.normcase(str(path.resolve()))
        if key not in seen:
            seen.add(key)
            unique.append(path)
    if not unique:
        raise MbmError("no MBM files found")
    return unique


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Decode MSG2/MBM files to XML.")
    parser.add_argument(
        "files", nargs="+", help="MBM files, directories, or recursive wildcard patterns"
    )
    parser.add_argument(
        "--sidecar", action="store_true", help="write FILE.mbm.xml next to each input"
    )
    parser.add_argument(
        "--out-dir", type=Path, help="write FILE.mbm.xml files under this directory"
    )
    parser.add_argument(
        "--raw",
        action="store_true",
        help="keep CP932 full-width text instead of normalizing to ASCII",
    )
    parser.add_argument(
        "--legacy-f81b-u32",
        action="store_true",
        help="render F81B as three u32 values, matching older XML sidecars",
    )
    output_mode = parser.add_mutually_exclusive_group()
    output_mode.add_argument(
        "--summary", action="store_true", help="print one structural summary per file"
    )
    output_mode.add_argument(
        "--validate", action="store_true", help="validate all inputs and print one corpus total"
    )
    args = parser.parse_args(argv)

    if args.out_dir and args.sidecar:
        parser.error("--out-dir and --sidecar are mutually exclusive")
    if args.validate and (args.out_dir or args.sidecar):
        parser.error("--validate cannot be combined with --out-dir or --sidecar")
    if args.summary and (args.out_dir or args.sidecar):
        parser.error("--summary cannot be combined with --out-dir or --sidecar")
    try:
        files = expand_input_paths(args.files)
    except MbmError as exc:
        parser.error(str(exc))

    if len(files) > 1 and not (args.out_dir or args.sidecar or args.summary or args.validate):
        parser.error("multiple input files require --sidecar, --out-dir, --summary, or --validate")

    input_root: Path | None = None
    if args.out_dir:
        input_root = Path(os.path.commonpath([str(path.resolve().parent) for path in files]))

    errors: list[str] = []
    total_entries = 0
    total_slots = 0
    total_text_bytes = 0
    for path in files:
        try:
            header, entries = parse_mbm(
                path, normalize=not args.raw, legacy_f81b=args.legacy_f81b_u32
            )
            total_entries += len(entries)
            total_slots += header.slot_count
            total_text_bytes += header.text_blob_size
            if args.validate:
                continue
            if args.summary:
                max_id = entries[-1].msg_id if entries else -1
                print(
                    f"{path}: entries={len(entries)} max_id={max_id} "
                    f"slots={header.slot_count} text_start=0x{header.text_start:X} "
                    f"text_blob=0x{header.text_blob_size:X} used_size=0x{header.used_size:X}"
                )
                continue
            xml = entries_to_xml(entries)
            if args.sidecar or args.out_dir:
                print(write_sidecar(path, xml, args.out_dir, input_root))
            else:
                write_stdout_utf8(xml)
        except (MbmError, OSError) as exc:
            if args.validate:
                errors.append(str(exc))
                continue
            print(exc, file=sys.stderr)
            return 1

    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        print(f"validation failed: {len(errors)} of {len(files)} file(s)", file=sys.stderr)
        return 1
    if args.validate:
        print(
            f"validated {len(files)} file(s): entries={total_entries} slots={total_slots} "
            f"text_bytes={total_text_bytes}"
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
