# Persona 5 Royal battle PAC validation

The `.pac` loader recognizes the existing old BIN layout: a 252-byte ASCII
name, a little-endian 32-bit payload size, payload data aligned to 64 bytes,
and a final zero-filled 256-byte header. FPAC archives retain their existing
reader. Internal entry paths are preserved by the BIN reader and writer.

Validated on 2026-09-10 against local files in
`D:\SteamLibrary\steamapps\common\P5R\CPK\ES\BATTLE`:

| File | Entries |
| --- | ---: |
| COMMAND.PAC | 17 |
| COMMANDPLG.PAC | 15 |
| HOLDUP.PAC | 67 |
| RESIDENT.PAC | 3 |
| RESULT.PAC | 18 |
| TABLE.PAC | 12 |

Checks used the built library from PowerShell (.NET 10). All archives opened
through `GameFormatHelper`, serialized with matching `GetSize()`, and reopened
with the same entry count. With original entry payloads represented as DAT,
all six archives round-tripped byte-for-byte (SHA-256). Replacing the first
entry with a five-byte DAT payload and reopening succeeded for all six.
An all-zero 512-byte input was rejected by the layout detector.

Normal serialization is byte-identical for COMMAND, COMMANDPLG, HOLDUP, and
RESULT. RESIDENT and TABLE differ under normal embedded-format serialization;
the raw-payload check isolates this from the archive layout. In-game behavior
and WPF interaction were not tested. Source game files were not modified or
added to the repository.

`dotnet build PersonaEditor.sln -c Debug -p:Platform=x64` passed with warnings.

Suggested automated regression coverage: synthetic archives with nested entry
paths, empty payloads, payload sizes on either side of a 64-byte boundary,
resized replacements, invalid lengths, truncated headers, nonzero terminators,
and an FPAC fixture to check dispatch compatibility. Use generated data rather
than game assets.
