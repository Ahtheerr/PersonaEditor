# MBM / MSG2 Format

This document describes every `*.mbm` file in this ROMFS dump, not only the files
under `shop`. The files are little-endian `MSG2` message banks with a sparse,
direct-address slot table keyed by message ID.

The companion parser is `shop/mbm_decode.py`. Its corpus test covers all 1,246
MBM files and all 37,158 active records in this dump.

## File layout

| Offset | Size | Type | Meaning |
| --- | ---: | --- | --- |
| `0x00` | 4 | `u32` | Zero. |
| `0x04` | 4 | bytes | Magic `MSG2`. |
| `0x08` | 4 | `u32` | Version `0x00010000`. |
| `0x0C` | 4 | `u32` | Logical used size. |
| `0x10` | 4 | `u32` | Active, non-empty row count. |
| `0x14` | 4 | `u32` | Slot table offset, `0x20`. |
| `0x18` | 8 | bytes | Zero padding. |
| `0x20` | variable | rows | Sparse slot table. |
| `text_start` | variable | bytes | Concatenated message records. |

The logical used size excludes all-zero sparse slots:

```text
used_size      = 0x20 + active_entry_count * 0x10 + text_blob_size
text_blob_size = used_size - 0x20 - active_entry_count * 0x10
text_start     = file_size - text_blob_size
slot_count     = (text_start - table_offset) / 0x10
```

Consequently:

```text
file_size - used_size = (slot_count - active_entry_count) * 0x10
```

`used_size == file_size` for 612 banks with no holes and is smaller for the 634
sparse banks. For example, `shop/shop_ginzaitm.mbm` has 17 active rows, a
highest ID of 72, 73 physical slots, and `text_start == 0x4B0`.

## Slot table row

Each physical slot is 16 bytes:

| Row offset | Size | Type | Meaning |
| --- | ---: | --- | --- |
| `+0x00` | 4 | `u32` | Message ID. In an active row this equals the zero-based slot index. |
| `+0x04` | 4 | `u32` | Record byte length, including the final `FF FF`. |
| `+0x08` | 4 | `u32` | Absolute file offset of the record. |
| `+0x0C` | 4 | `u32` | Reserved; zero throughout this corpus. |

An unused slot is 16 zero bytes. An active ID 0 row is not ambiguous because its
length and offset fields are nonzero. The last physical slot is the highest active
message ID, so a non-empty bank has `slot_count == max_id + 1`.

Active records occur in slot order and exactly tile `[text_start, file_size)`:
there are no gaps, overlaps, aliases, or trailing bytes in any file in the dump.
The 28 empty banks are exactly 32-byte headers with zero rows and no text blob.

## Message records

Every record ends in `FF FF`, and the row length includes those two bytes. Text is
CP932, the Windows Shift-JIS variant. The localized English text is mostly stored
with full-width Latin glyphs, so raw ASCII searches often miss it.

At a character boundary, a record can contain:

| Bytes | Meaning |
| --- | --- |
| `FF FF` | Record terminator. It is valid only at the record end. |
| `00 00` | Inline null marker, rendered as `\0` in an XML sidecar. |
| `F8 opcode ...` | Main control sequence. |
| `80 opcode ...` | Alternate control sequence used by a small set of Japanese/test records. |
| otherwise | One CP932 character. |

Control introducers must only be recognized at a CP932 character boundary. In
particular, bytes in the `F0`-`FC` range can be CP932 lead bytes and `80` can be a
trail byte. The parser advances over complete CP932 characters, so it does not
mistake trail bytes for controls.

By default, the decoder follows the existing XML convention: full-width ASCII and
the ideographic space are narrowed, curly quotes are mapped to ASCII quotes, the
CP932 ellipsis becomes `...`, and the full-width yen sign becomes `U+00A5`.
`--raw` preserves decoded CP932 text without these display normalizations.

## `F8` controls

The following table is exhaustive for this dump. Payload integers are
little-endian. Counts are included to distinguish established layouts from
one-off observations.

| XML form | Payload after opcode | Count | Observed role |
| --- | --- | ---: | --- |
| `{F801}` | none | 81,616 | Line/message advance. |
| `{F802}` | none | 25,545 | Page/paragraph separator. |
| `{F804,a}` | one `u16` | 4,760 | Text style/color state. |
| `{F811,a}` | one `u16` | 102 | Numbered UI variable. |
| `{F812}` | none | 10,939 | Usually introduces a speaker field ending in `00 00`. |
| `{F813,a,b}` | two `u32` | 1,358 | Two-integer lookup/runtime variable. |
| `{F814,a,b}` | two `u16` | 57 | Button/glyph selector. |
| `{F81B,id}` | 8-byte ID plus one `u32` | 11,600 | Text/event identifier; detailed below. |
| `{F842,a}` | one `u16` | 2 | Numbered variable. |
| `{F843,a}` | one `u16` | 913 | Player/ally variable. |
| `{F871,a}` | one `u16` | 13,962 | Message-mode variable; always 1 here. |
| `{F872,a}` | one `u16` | 843 | Item variable. |
| `{F873,a}` | one `u16` | 376 | Demon/name variable. |
| `{F874,a}` | one `u16` | 2 | Numbered variable paired with `F842`. |
| `{F875,a}` | one `u16` | 136 | Demon-race variable. |
| `{F877,a}` | one `u16` | 224 | Skill variable. |
| `{F878,a}` | one `u16` | 578 | Amount variable. |
| `{F87A,a}` | one `u16` | 8,880 | Runtime speaker/message variable. |
| `{F87B,a,b,c,d}` | four `u16` | 1,689 | Four-field runtime lookup. |
| `{F87D,a}` | one `u16` | 3,449 | Numbered UI/runtime variable. |
| `{F87E}` | none | 55 | Parameterless battle-event marker. |

The 12-byte `F81B` payload is:

| Payload offset | Size | Type | Meaning |
| --- | ---: | --- | --- |
| `+0x00` | 8 | ASCII | Six-, seven-, or eight-character identifier, NUL-padded. |
| `+0x08` | 4 | `u32` | Reserved/tail value; zero in all 11,600 occurrences. |

Example:

```text
F8 1B 45 30 30 31 30 30 31 00 00 00 00 00
=> {F81B,E001001}
```

Six-character IDs have two NUL padding bytes, seven-character IDs have one, and
eight-character IDs fill the field. The corpus contains 89, 10,909, and 602 of
these respectively. `--legacy-f81b-u32` renders the same 12 payload bytes as three
decimal `u32` values to reproduce older sidecars exactly.

## `80` controls

The alternate `80` introducer occurs at control boundaries in 13 files. Its
opcode layouts must not be assumed to match `F8`; for example, `F813` has two
`u32` arguments while `8013` has one `u16`.

| XML form | Payload after opcode | Count | Observed role |
| --- | --- | ---: | --- |
| `{8001}` | none | 262 | Line/message advance. |
| `{8002}` | none | 76 | Page/paragraph separator. |
| `{8004,a}` | one `u16` | 84 | Text style/color state. |
| `{8012}` | none | 83 | Speaker field introducer. |
| `{8013,a}` | one `u16` | 5 | Numbered text mode. |
| `{8016}` | none | 42 | Start furigana base text. |
| `{8017}` | none | 42 | Separate base text from its reading. |
| `{8018}` | none | 42 | End furigana reading. |
| `{8041,a}` | one `u16` | 1 | Numbered variable. |
| `{8042,a}` | one `u16` | 1 | Numbered variable. |
| `{8043,a}` | one `u16` | 4 | Player/ally variable. |
| `{8051,a}` through `{8058,a}` | one `u16` each | 1 each | Numbered style/test variables. |
| `{807A,a}` | one `u16` | 2 | Runtime speaker/message variable. |

The furigana interpretation is visible directly in records such as:

```text
80 16 93 C5 80 17 82 C7 82 AD 80 18
=> {8016}<poison kanji>{8017}<doku in hiragana>{8018}
```

The byte sequences decode to the kanji for "poison" and its hiragana reading;
the ASCII labels above avoid depending on a terminal's Japanese font support.

## Decoder and validation

Run these examples from the ROMFS root:

```powershell
python .\shop\mbm_decode.py .\shop\shop_ginzaitm.mbm
python .\shop\mbm_decode.py .\shop --sidecar
python .\shop\mbm_decode.py . --validate
python .\shop\mbm_decode.py . --summary
python .\shop\mbm_decode.py .\shop\shop_ginzaitm.mbm --legacy-f81b-u32
python -m unittest .\shop\test_mbm_decode.py
```

Directories are searched recursively. Recursive glob patterns are also supported.
`--out-dir` mirrors the common input directory structure instead of flattening
duplicate filenames.

The parser validates all of the following before accepting a bank:

- Header prefix, magic, version, table offset, zero padding, and size arithmetic.
- Logical used size does not exceed the physical file size.
- Sparse table alignment and active row count.
- Active row ID equals its physical slot index.
- Reserved row fields are zero and each record range is in the text blob.
- Records are in slot order and exactly cover the complete text blob.
- Every record has exactly one final `FF FF` and no early terminator.
- CP932 byte sequences and inline `00 00` markers are well formed.
- Every control opcode and payload width is known for its introducer.
- `F81B` identifiers are printable ASCII with valid NUL padding.
- Generated XML is well formed for every record in the corpus test.
- Raw decoded records re-encode byte-for-byte to every original record.

Current exhaustive result:

```text
validated 1246 file(s): entries=37158 slots=467075 text_bytes=4899518
```

Across those records the parser consumes 167,086 `F8` controls and 652 `80`
controls without fallback bytes or unknown-opcode guessing.
