using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditorLib.Text
{
    public class MBM : IGameData
    {
        private const int HeaderSize = 0x20;
        private const int RowSize = 0x10;
        private const uint Version = 0x00010000;
        private const string BundleMagic = "TBB1";
        private const string RawNoAutoPrefix = "[RN]";
        private const string LineBreakToken = "{F801}";

        private static readonly Encoding ShiftJis = Encoding.GetEncoding("shift-jis");
        private static readonly Dictionary<byte, int> ControlU16Counts = new Dictionary<byte, int>
        {
            { 0x01, 0 },
            { 0x02, 0 },
            { 0x04, 1 },
            { 0x05, 1 },
            { 0x11, 1 },
            { 0x12, 0 },
            { 0x13, 4 },
            { 0x14, 2 },
            { 0x43, 1 },
            { 0x44, 0 },
            { 0x45, 1 },
            { 0x47, 1 },
            { 0x4A, 1 },
            { 0x4B, 1 },
            { 0x4C, 1 },
            { 0x4D, 1 },
            { 0x50, 1 },
            { 0x51, 1 },
            { 0x52, 1 },
            { 0x53, 1 },
            { 0x54, 1 },
            { 0x55, 1 },
            { 0x56, 1 },
            { 0x57, 1 },
            { 0x58, 1 },
            { 0x59, 1 },
            { 0x5A, 1 },
            { 0x5B, 1 },
            { 0x5C, 0 },
            { 0x5D, 0 },
            { 0x5E, 1 },
            { 0x5F, 1 },
            { 0x60, 0 },
            { 0x61, 0 },
            { 0x63, 0 },
            { 0x64, 1 },
            { 0x6A, 0 },
            { 0x6B, 0 },
            { 0x6C, 0 },
            { 0x6D, 0 },
            { 0x70, 1 },
            { 0x71, 1 },
            { 0x72, 1 },
            { 0x73, 1 },
            { 0x75, 1 },
            { 0x77, 1 },
            { 0x78, 1 },
            { 0x7A, 1 },
            { 0x7B, 4 },
            { 0x7D, 1 }
        };

        private readonly byte[] originalData;
        private readonly List<MBMEntry> entries = new List<MBMEntry>();
        private readonly List<MBMSection> sections = new List<MBMSection>();
        private bool isBundle;
        private int bundleAlignment;

        public MBM(byte[] data)
        {
            originalData = data?.ToArray() ?? throw new ArgumentNullException(nameof(data));
            Read(originalData);
        }

        public IReadOnlyList<MBMEntry> Entries => entries;
        public FormatEnum Type => FormatEnum.MBM;
        public List<GameFile> SubFiles { get; } = new List<GameFile>();
        public int GetSize() => GetData().Length;

        public byte[] GetData()
        {
            if (entries.All(x => !x.HasChanges))
                return originalData.ToArray();

            byte[][] sectionData = sections
                .Select(section => BuildSection(section, isBundle ? bundleAlignment : 1))
                .ToArray();

            if (!isBundle)
                return sectionData[0];

            int headerSize = Align(0x10 + sectionData.Length * sizeof(int), bundleAlignment);
            int totalSize = checked(headerSize + sectionData.Sum(x => x.Length));
            using MemoryStream ms = new MemoryStream(totalSize);
            using BinaryWriter writer = new BinaryWriter(ms, Encoding.ASCII, true);
            writer.Write(Encoding.ASCII.GetBytes(BundleMagic));
            writer.Write(bundleAlignment);
            writer.Write(sectionData.Length);
            writer.Write(totalSize);

            int sectionOffset = headerSize;
            foreach (byte[] data in sectionData)
            {
                writer.Write(sectionOffset);
                sectionOffset += data.Length;
            }

            writer.Write(new byte[headerSize - checked((int)ms.Position)]);
            foreach (byte[] data in sectionData)
                writer.Write(data);

            return ms.ToArray();
        }

        private static byte[] BuildSection(MBMSection section, int alignment)
        {
            byte[][] records = section.Entries.Select(x => x.GetData()).ToArray();
            int textStart = checked(HeaderSize + section.SlotCount * RowSize);
            int unalignedSize = checked(textStart + records.Sum(x => x.Length));
            int sectionSize = Align(unalignedSize, alignment);
            int declaredCount = section.CountIsSlotCount ? section.SlotCount : section.Entries.Count;
            int usedSize = section.CountIsSlotCount
                ? sectionSize
                : checked(HeaderSize + declaredCount * RowSize + records.Sum(x => x.Length));

            using MemoryStream ms = new MemoryStream(sectionSize);
            using BinaryWriter writer = new BinaryWriter(ms, Encoding.ASCII, true);
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("MSG2"));
            writer.Write(Version);
            writer.Write(usedSize);
            writer.Write(declaredCount);
            writer.Write(HeaderSize);
            writer.Write(0L);
            writer.Write(new byte[section.SlotCount * RowSize]);

            int textOffset = textStart;
            for (int i = 0; i < section.Entries.Count; i++)
            {
                MBMEntry entry = section.Entries[i];
                byte[] record = records[i];
                ms.Position = HeaderSize + entry.RecordId * RowSize;
                writer.Write(entry.RecordId);
                writer.Write(record.Length);
                writer.Write(textOffset);
                writer.Write(entry.Unknown);

                ms.Position = textOffset;
                writer.Write(record);
                textOffset += record.Length;
            }

            ms.SetLength(sectionSize);
            return ms.ToArray();
        }

        private static int Align(int value, int alignment)
        {
            if (alignment <= 1)
                return value;

            return checked((value + alignment - 1) & ~(alignment - 1));
        }

        public string[] ExportText(string fileName, bool removeSplit)
        {
            return entries
                .SelectMany(entry => entry.Strings.Select(text =>
                    $"{fileName}\t{entry.Id}\t{EscapeText(text.IdentifierOrIndex, removeSplit)}\t{EscapeText(entry.Name, removeSplit)}\t{EscapeText(text.OldText, removeSplit)}\t"))
                .ToArray();
        }

        public void ImportTextById(IEnumerable<(int Id, string Text)> importedText, Dictionary<char, int> charWidth = null, int width = 0)
        {
            if (importedText == null)
                return;

            Dictionary<int, MBMEntry> byId = entries.ToDictionary(x => x.Id);
            foreach (var item in importedText)
            {
                if (!byId.TryGetValue(item.Id, out MBMEntry entry) || string.IsNullOrEmpty(item.Text))
                    continue;

                entry.NewText = PrepareImportedText(item.Text, charWidth, width);
            }
        }

        public void ImportTextByString(IEnumerable<(int Id, string Identifier, string Text)> importedText, Dictionary<char, int> charWidth = null, int width = 0)
        {
            if (importedText == null)
                return;

            Dictionary<int, MBMEntry> byId = entries.ToDictionary(x => x.Id);
            foreach (var item in importedText)
            {
                if (!byId.TryGetValue(item.Id, out MBMEntry entry) || string.IsNullOrEmpty(item.Text))
                    continue;

                MBMString text = entry.FindString(item.Identifier);
                if (text == null)
                    continue;

                text.NewText = PrepareImportedText(item.Text, charWidth, width);
            }
        }

        private void Read(byte[] data)
        {
            entries.Clear();
            sections.Clear();

            if (HasMagic(data, 0, BundleMagic))
            {
                ReadBundle(data);
                return;
            }

            isBundle = false;
            sections.Add(ReadSection(data, 0, RecordFormat.Legacy, false));
        }

        private void ReadBundle(byte[] data)
        {
            if (data.Length < 0x20)
                throw new Exception("MBM: TBB1 bundle is too small");

            bundleAlignment = checked((int)ReadUInt32(data, 0x04));
            int sectionCount = checked((int)ReadUInt32(data, 0x08));
            int totalSize = checked((int)ReadUInt32(data, 0x0C));
            if (bundleAlignment <= 0 || (bundleAlignment & (bundleAlignment - 1)) != 0)
                throw new Exception("MBM: invalid TBB1 alignment");
            if (sectionCount <= 0 || 0x10L + sectionCount * sizeof(int) > data.Length)
                throw new Exception("MBM: invalid TBB1 section count");
            if (totalSize != data.Length)
                throw new Exception("MBM: TBB1 size does not match the file length");

            int minimumOffset = Align(0x10 + sectionCount * sizeof(int), bundleAlignment);
            int[] offsets = new int[sectionCount];
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = checked((int)ReadUInt32(data, 0x10 + i * sizeof(int)));
                if (offsets[i] < minimumOffset || offsets[i] >= data.Length || offsets[i] % bundleAlignment != 0)
                    throw new Exception("MBM: invalid TBB1 section offset");
                if (i > 0 && offsets[i] <= offsets[i - 1])
                    throw new Exception("MBM: TBB1 section offsets are not ascending");
            }

            isBundle = true;
            int virtualIdBase = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                int end = i + 1 < offsets.Length ? offsets[i + 1] : data.Length;
                byte[] sectionData = data.AsSpan(offsets[i], end - offsets[i]).ToArray();
                MBMSection section = ReadSection(sectionData, virtualIdBase, RecordFormat.NullTerminated, true);
                sections.Add(section);
                virtualIdBase = checked(virtualIdBase + section.SlotCount);
            }
        }

        private MBMSection ReadSection(byte[] data, int virtualIdBase, RecordFormat recordFormat, bool countIsSlotCount)
        {
            if (data.Length < HeaderSize)
                throw new Exception("MBM: file too small");
            if (ReadUInt32(data, 0x00) != 0)
                throw new Exception("MBM: expected zero prefix");
            if (!HasMagic(data, 0x04, "MSG2"))
                throw new Exception("MBM: wrong magic number");
            if (ReadUInt32(data, 0x08) != Version)
                throw new Exception("MBM: unsupported version");

            int usedSize = checked((int)ReadUInt32(data, 0x0C));
            int entryCount = checked((int)ReadUInt32(data, 0x10));
            int tableOffset = checked((int)ReadUInt32(data, 0x14));
            if (tableOffset != HeaderSize)
                throw new Exception("MBM: unsupported table offset");
            if (ReadUInt32(data, 0x18) != 0 || ReadUInt32(data, 0x1C) != 0)
                throw new Exception("MBM: expected zero header padding");
            if (usedSize > data.Length)
                throw new Exception("MBM: used size exceeds the section length");

            int slotCount;
            int textStart;
            if (countIsSlotCount)
            {
                slotCount = entryCount;
                textStart = checked(tableOffset + slotCount * RowSize);
                if (textStart > usedSize)
                    throw new Exception("MBM: table exceeds the section length");
            }
            else
            {
                int textBlobSize = usedSize - HeaderSize - entryCount * RowSize;
                if (textBlobSize < 0)
                    throw new Exception("MBM: invalid used size");

                textStart = data.Length - textBlobSize;
                int tableSize = textStart - tableOffset;
                if (tableSize < 0 || tableSize % RowSize != 0)
                    throw new Exception("MBM: invalid sparse table size");
                slotCount = tableSize / RowSize;
            }

            var sectionEntries = new List<MBMEntry>();

            for (int slot = 0; slot < slotCount; slot++)
            {
                int rowOffset = tableOffset + slot * RowSize;
                int id = checked((int)ReadUInt32(data, rowOffset));
                int byteLength = checked((int)ReadUInt32(data, rowOffset + 4));
                int textOffset = checked((int)ReadUInt32(data, rowOffset + 8));
                int unknown = checked((int)ReadUInt32(data, rowOffset + 12));

                if (id == 0 && byteLength == 0 && textOffset == 0 && unknown == 0)
                    continue;
                if (id != slot)
                    throw new Exception("MBM: row id does not match slot index");
                if (byteLength <= 0 || textOffset < textStart || textOffset + byteLength > data.Length)
                    throw new Exception("MBM: entry points outside the text blob");

                byte[] record = data.Skip(textOffset).Take(byteLength).ToArray();
                var entry = new MBMEntry(checked(virtualIdBase + id), id, byteLength, textOffset,
                    unknown, record, DecodeRecord(record, recordFormat), recordFormat);
                sectionEntries.Add(entry);
                entries.Add(entry);
            }

            if (!countIsSlotCount && sectionEntries.Count != entryCount)
                throw new Exception("MBM: active entry count mismatch");

            return new MBMSection(slotCount, countIsSlotCount, sectionEntries);
        }

        private static string DecodeRecord(byte[] record, RecordFormat recordFormat)
        {
            if (record.Length < 2 || !IsTerminator(record, record.Length - 2, recordFormat))
                throw new Exception(recordFormat == RecordFormat.Legacy
                    ? "MBM: message record does not end with FF FF"
                    : "MBM: message record does not end with 00 00");

            StringBuilder output = new StringBuilder();
            int position = 0;
            while (position < record.Length)
            {
                if (IsTerminator(record, position, recordFormat))
                {
                    if (position != record.Length - 2)
                        throw new Exception("MBM: record terminator appears before the end");
                    break;
                }

                if (recordFormat == RecordFormat.Legacy && IsInlineNull(record, position))
                {
                    output.Append("\\0");
                    position += 2;
                    continue;
                }

                if (record[position] == 0xF8)
                {
                    DecodeControl(record, ref position, output, recordFormat);
                    continue;
                }

                int start = position;
                while (position < record.Length
                    && !IsTerminator(record, position, recordFormat)
                    && !(recordFormat == RecordFormat.Legacy && IsInlineNull(record, position))
                    && record[position] != 0xF8)
                {
                    byte value = record[position];
                    if (value < 0x80 || (value >= 0xA1 && value <= 0xDF))
                        position++;
                    else
                        position += 2;
                }

                if (position > record.Length)
                    throw new Exception("MBM: truncated CP932 character");

                output.Append(NormalizeCp932(ShiftJis.GetString(record, start, position - start)));
            }

            return output.ToString();
        }

        private static void DecodeControl(byte[] record, ref int position, StringBuilder output, RecordFormat recordFormat)
        {
            if (position + 2 > record.Length)
                throw new Exception("MBM: truncated control opcode");

            byte opcode = record[position + 1];
            position += 2;

            if (opcode == 0x1B)
            {
                if (recordFormat == RecordFormat.NullTerminated)
                {
                    int identifierEnd = Array.IndexOf(record, (byte)0, position);
                    if (identifierEnd < position)
                        throw new Exception("MBM: invalid null-terminated F81B identifier");

                    ReadOnlySpan<byte> identifierBytes = record.AsSpan(position, identifierEnd - position);
                    bool invalidIdentifier = identifierBytes.Length == 0;
                    for (int i = 0; i < identifierBytes.Length && !invalidIdentifier; i++)
                        invalidIdentifier = identifierBytes[i] < 0x20 || identifierBytes[i] >= 0x7F;
                    if (invalidIdentifier)
                        throw new Exception("MBM: invalid F81B identifier");

                    int zeroCount = 2 + (identifierBytes.Length & 1);
                    if (identifierEnd + zeroCount > record.Length)
                        throw new Exception("MBM: truncated F81B trailer");
                    for (int i = 0; i < zeroCount; i++)
                        if (record[identifierEnd + i] != 0)
                            throw new Exception("MBM: invalid F81B trailer");

                    output.Append($"{{F81B,{Encoding.ASCII.GetString(identifierBytes)}}}");
                    position = identifierEnd + zeroCount;
                    return;
                }

                if (position + 12 > record.Length)
                    throw new Exception("MBM: truncated F81B payload");

                ReadOnlySpan<byte> payload = record.AsSpan(position, 12);
                ReadOnlySpan<byte> identBytes = payload.Slice(0, 8);
                uint tail = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));

                if (TryDecodeIdentifier(identBytes, out string identifier))
                    output.Append(tail == 0 ? $"{{F81B,{identifier}}}" : $"{{F81B,{identifier},{tail}}}");
                else
                    output.Append($"{{F81B,{BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4))},{BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4))},{tail}}}");

                position += 12;
                return;
            }

            if (!ControlU16Counts.TryGetValue(opcode, out int count))
                throw new Exception($"MBM: unknown control opcode F8{opcode:X2}");

            ushort[] args = new ushort[count];
            for (int i = 0; i < args.Length; i++)
            {
                if (position + 2 > record.Length)
                    throw new Exception("MBM: truncated control argument");
                args[i] = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(position, 2));
                position += 2;
            }

            output.Append(args.Length == 0
                ? $"{{F8{opcode:X2}}}"
                : $"{{F8{opcode:X2},{string.Join(",", args)}}}");
        }

        private static byte[] EncodeRecord(string text, RecordFormat recordFormat)
        {
            text ??= string.Empty;

            using MemoryStream ms = new MemoryStream();
            for (int i = 0; i < text.Length;)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    if (text[i + 1] == '0')
                    {
                        ms.WriteByte(0);
                        ms.WriteByte(0);
                        i += 2;
                        continue;
                    }

                    if (text[i + 1] == 'n')
                    {
                        ms.WriteByte(0xF8);
                        ms.WriteByte(0x01);
                        i += 2;
                        continue;
                    }
                }
                else if (text[i] == '\\')
                {
                    WritePlainText(ms, text.Substring(i, 1));
                    i++;
                    continue;
                }

                if (text[i] == '{')
                {
                    int end = text.IndexOf('}', i + 1);
                    using MemoryStream tokenStream = new MemoryStream();
                    if (end > i && TryEncodeToken(text.Substring(i + 1, end - i - 1), tokenStream, recordFormat))
                    {
                        tokenStream.WriteTo(ms);
                        i = end + 1;
                        continue;
                    }

                    WritePlainText(ms, text.Substring(i, 1));
                    i++;
                    continue;
                }

                if (text[i] == '\r' || text[i] == '\n')
                {
                    if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;

                    ms.WriteByte(0xF8);
                    ms.WriteByte(0x01);
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && text[i] != '{' && text[i] != '\\' && text[i] != '\r' && text[i] != '\n')
                    i++;
                WritePlainText(ms, text.Substring(start, i - start));
            }

            byte terminator = recordFormat == RecordFormat.Legacy ? (byte)0xFF : (byte)0;
            ms.WriteByte(terminator);
            ms.WriteByte(terminator);
            return ms.ToArray();
        }

        private static void WritePlainText(Stream stream, string text)
        {
            byte[] plain = ShiftJis.GetBytes(ToFullWidthCp932Text(text));
            stream.Write(plain, 0, plain.Length);
        }

        private static string ToFullWidthCp932Text(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            StringBuilder builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == ' ')
                    builder.Append('\u3000');
                else if (c >= 0x21 && c <= 0x7E)
                    builder.Append((char)(c + 0xFEE0));
                else
                    builder.Append(c);
            }

            return builder.ToString();
        }

        private static bool TryEncodeToken(string token, Stream output, RecordFormat recordFormat)
        {
            token = token.Trim();
            string[] parts = token.Split(',').Select(x => x.Trim()).ToArray();

            if (parts.Length > 1 && TryParseControlCode(parts[0], out byte opcode))
            {
                output.WriteByte(0xF8);
                output.WriteByte(opcode);

                if (opcode == 0x1B)
                    return recordFormat == RecordFormat.Legacy
                        ? TryWriteF81B(parts, output)
                        : TryWriteNullTerminatedF81B(parts, output);

                if (!ControlU16Counts.TryGetValue(opcode, out int count) || count != parts.Length - 1)
                    return false;

                for (int i = 1; i < parts.Length; i++)
                {
                    if (!TryParseUInt32(parts[i], out uint value) || value > ushort.MaxValue)
                        return false;
                    WriteUInt16(output, (ushort)value);
                }

                return true;
            }

            if (parts.Length == 1 && TryParseControlCode(parts[0], out opcode)
                && ControlU16Counts.TryGetValue(opcode, out int argCount) && argCount == 0)
            {
                output.WriteByte(0xF8);
                output.WriteByte(opcode);
                return true;
            }

            return parts.Length == 1 && TryWriteRawHex(parts[0], output);
        }

        private static bool TryWriteNullTerminatedF81B(string[] parts, Stream output)
        {
            if (parts.Length != 2)
                return false;

            string identifier = parts[1];
            if (identifier.Length == 0 || identifier.Any(x => x < 0x20 || x >= 0x7F))
                return false;

            byte[] identifierBytes = Encoding.ASCII.GetBytes(identifier);
            output.Write(identifierBytes, 0, identifierBytes.Length);
            output.WriteByte(0);
            output.WriteByte(0);
            if ((identifierBytes.Length & 1) != 0)
                output.WriteByte(0);
            return true;
        }

        private static bool TryWriteF81B(string[] parts, Stream output)
        {
            if (parts.Length == 4
                && TryParseUInt32(parts[1], out uint first)
                && TryParseUInt32(parts[2], out uint second)
                && TryParseUInt32(parts[3], out uint third))
            {
                WriteUInt32(output, first);
                WriteUInt32(output, second);
                WriteUInt32(output, third);
                return true;
            }

            if (parts.Length < 2 || parts.Length > 3)
                return false;

            string identifier = parts[1];
            if (identifier.Length == 0 || identifier.Length > 8 || identifier.Any(x => x < 0x20 || x >= 0x7F))
                return false;

            byte[] idBytes = new byte[8];
            Encoding.ASCII.GetBytes(identifier, 0, identifier.Length, idBytes, 0);
            output.Write(idBytes, 0, idBytes.Length);

            uint tail = 0;
            if (parts.Length == 3 && !TryParseUInt32(parts[2], out tail))
                return false;

            WriteUInt32(output, tail);
            return true;
        }

        private static bool TryWriteRawHex(string token, Stream output)
        {
            string compact = token.Replace(" ", "").Replace("\u00A0", "");
            if (compact.Length == 0 || compact.Length % 2 != 0)
                return false;

            for (int i = 0; i < compact.Length; i += 2)
            {
                if (!byte.TryParse(compact.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                    return false;
                output.WriteByte(value);
            }

            return true;
        }

        private static bool TryParseControlCode(string value, out byte opcode)
        {
            opcode = 0;
            value = value.Trim();
            if (value.Length != 4 || !value.StartsWith("F8", StringComparison.OrdinalIgnoreCase))
                return false;

            return byte.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out opcode);
        }

        private static bool TryParseUInt32(string value, out uint result)
        {
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

            return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryDecodeIdentifier(ReadOnlySpan<byte> data, out string identifier)
        {
            int length = data.IndexOf((byte)0);
            if (length < 0)
                length = data.Length;

            if (length == 0)
            {
                identifier = "";
                return false;
            }

            for (int i = 0; i < data.Length; i++)
            {
                byte value = data[i];
                if (value != 0 && (value < 0x20 || value >= 0x7F))
                {
                    identifier = "";
                    return false;
                }
            }

            identifier = Encoding.ASCII.GetString(data.Slice(0, length));
            return true;
        }

        private static string NormalizeCp932(string text)
        {
            return text.Normalize(NormalizationForm.FormKC)
                .Replace('\u2018', '\'')
                .Replace('\u2019', '\'')
                .Replace('\u201C', '"')
                .Replace('\u201D', '"');
        }

        private static string EscapeText(string text, bool removeSplit)
        {
            text ??= string.Empty;
            text = ReplaceLineBreakToken(text, removeSplit ? " " : LineBreakToken);
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return removeSplit ? text.Replace("\n", " ") : text.Replace("\n", "\\n");
        }

        private static string PrepareImportedText(string text, Dictionary<char, int> charWidth, int width)
        {
            text ??= string.Empty;
            string prepared = width > 0 && charWidth != null && !IsImportedRaw(text)
                ? text.SplitByWidth(charWidth, width)
                : text.NormalizeImportedText();

            return NormalizeLineBreaksToToken(prepared);
        }

        private static bool IsImportedRaw(string text)
            => text != null && text.StartsWith(RawNoAutoPrefix, StringComparison.OrdinalIgnoreCase);

        private static string NormalizeLineBreaksToToken(string text)
        {
            text ??= string.Empty;
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", LineBreakToken);
        }

        private static string ReplaceLineBreakToken(string text, string replacement)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length;)
            {
                if (IsLineBreakTokenAt(text, i))
                {
                    builder.Append(replacement);
                    i += LineBreakToken.Length;
                }
                else
                {
                    builder.Append(text[i]);
                    i++;
                }
            }

            return builder.ToString();
        }

        private static bool IsLineBreakTokenAt(string text, int index)
            => index + LineBreakToken.Length <= text.Length
            && string.Compare(text, index, LineBreakToken, 0, LineBreakToken.Length, StringComparison.OrdinalIgnoreCase) == 0;

        private static bool HasMagic(byte[] data, int offset, string magic)
            => data.Length >= offset + magic.Length && Encoding.ASCII.GetString(data, offset, magic.Length) == magic;

        private static bool IsTerminator(byte[] data, int offset, RecordFormat recordFormat)
        {
            byte value = recordFormat == RecordFormat.Legacy ? (byte)0xFF : (byte)0;
            return offset + 1 < data.Length && data[offset] == value && data[offset + 1] == value;
        }

        private static bool IsInlineNull(byte[] data, int offset)
            => offset + 1 < data.Length && data[offset] == 0 && data[offset + 1] == 0;

        private static uint ReadUInt32(byte[] data, int offset)
            => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

        private static void WriteUInt16(Stream stream, ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        internal enum RecordFormat
        {
            Legacy,
            NullTerminated
        }

        private sealed class MBMSection
        {
            public int SlotCount { get; }
            public bool CountIsSlotCount { get; }
            public IReadOnlyList<MBMEntry> Entries { get; }

            public MBMSection(int slotCount, bool countIsSlotCount, IReadOnlyList<MBMEntry> entries)
            {
                SlotCount = slotCount;
                CountIsSlotCount = countIsSlotCount;
                Entries = entries;
            }
        }

        public class MBMEntry
        {
            private readonly byte[] raw;
            private readonly byte[] rawNamePrefix;
            private readonly RecordFormat recordFormat;

            public int Id { get; }
            internal int RecordId { get; }
            public int ByteLength { get; }
            public int Offset { get; }
            public int Unknown { get; }
            public string Name { get; }
            public string CurrentName => NewName ?? Name;
            public bool HasNamePrefix => rawNamePrefix != null;
            public string OldText { get; }
            public IReadOnlyList<MBMString> Strings { get; }
            public string NewText { get; set; } = string.Empty;
            public string NewName { get; set; }
            public bool HasChanges => (!string.IsNullOrEmpty(NewText) && NewText != OldText)
                || (NewName != null && NewName != Name)
                || Strings.Any(x => x.HasChanges);

            public MBMEntry(int id, int byteLength, int offset, int unknown, byte[] raw, string decodedText)
                : this(id, id, byteLength, offset, unknown, raw, decodedText, RecordFormat.Legacy)
            {
            }

            internal MBMEntry(int id, int recordId, int byteLength, int offset, int unknown,
                byte[] raw, string decodedText, RecordFormat recordFormat)
            {
                Id = id;
                RecordId = recordId;
                ByteLength = byteLength;
                Offset = offset;
                Unknown = unknown;
                this.raw = raw?.ToArray() ?? throw new ArgumentNullException(nameof(raw));
                this.recordFormat = recordFormat;
                rawNamePrefix = GetRawNamePrefix(raw);
                SplitNamePrefix(decodedText ?? string.Empty, out string name, out string body);
                Name = name;
                OldText = body;
                Strings = ParseStrings(body);
            }

            public byte[] GetData()
            {
                if (!HasChanges)
                    return raw.ToArray();

                byte[] namePrefix = NewName != null && NewName != Name
                    ? EncodeNamePrefix(NewName, recordFormat)
                    : rawNamePrefix;

                if (!string.IsNullOrEmpty(NewText) && NewText != OldText)
                {
                    if (rawNamePrefix == null || NewText.StartsWith("{F812}", StringComparison.OrdinalIgnoreCase))
                        return EncodeRecord(NewText, recordFormat);

                    byte[] overrideBody = EncodeRecord(NewText, recordFormat);
                    return CombineNamePrefix(namePrefix, overrideBody);
                }

                if (NewName != null && NewName != Name && rawNamePrefix != null && !Strings.Any(x => x.HasChanges || x.HasEmbeddedNamePrefix))
                    return CombineNamePrefix(namePrefix, raw.Skip(rawNamePrefix.Length).ToArray());

                string replacementName = NewName != null && NewName != Name ? NewName : null;
                string rebuiltText = string.Concat(Strings.Select(x => x.GetText(Name, replacementName)));
                if (rawNamePrefix == null)
                    return EncodeRecord(rebuiltText, recordFormat);

                byte[] rebuiltBody = EncodeRecord(rebuiltText, recordFormat);
                return CombineNamePrefix(namePrefix, rebuiltBody);
            }

            public MBMString FindString(string identifier)
            {
                if (string.IsNullOrEmpty(identifier))
                    return Strings.Count == 1 ? Strings[0] : null;

                return Strings.FirstOrDefault(x =>
                    x.Identifier.Equals(identifier, StringComparison.CurrentCultureIgnoreCase)
                    || x.Index.ToString(CultureInfo.InvariantCulture).Equals(identifier, StringComparison.CurrentCultureIgnoreCase));
            }

            private static IReadOnlyList<MBMString> ParseStrings(string body)
            {
                var result = new List<MBMString>();
                int index = 0;
                int position = 0;

                while (position <= body.Length)
                {
                    int separatorStart = FindControlToken(body, position, "F802", out int separatorEnd);
                    string part = separatorStart >= 0 ? body.Substring(position, separatorStart - position) : body.Substring(position);
                    string separator = separatorStart >= 0 ? body.Substring(separatorStart, separatorEnd - separatorStart) : string.Empty;

                    result.Add(CreateString(index++, part, separator));

                    if (separatorStart < 0)
                        break;
                    position = separatorEnd;
                }

                return result;
            }

            private static MBMString CreateString(int index, string text, string separator)
            {
                int start = FindFirstTextIndex(text);
                if (start < 0)
                    return new MBMString(index, GetStringIdentifier(text), text + separator, string.Empty, string.Empty);

                int end = FindLastTextEnd(text);
                string prefix = text.Substring(0, start);
                string oldText = text.Substring(start, end - start);
                string suffix = text.Substring(end) + separator;
                return new MBMString(index, GetStringIdentifier(text), prefix, oldText, suffix);
            }

            private static int FindFirstTextIndex(string text)
            {
                for (int i = 0; i < text.Length;)
                {
                    if (text[i] == '{')
                    {
                        int end = text.IndexOf('}', i + 1);
                        if (end > i)
                        {
                            string token = text.Substring(i + 1, end - i - 1);
                            string tokenCode = token.Split(',')[0].Trim();
                            if (tokenCode.Equals("F812", StringComparison.OrdinalIgnoreCase))
                            {
                                int nameEnd = text.IndexOf("\\0", end + 1, StringComparison.Ordinal);
                                if (nameEnd >= 0)
                                {
                                    i = nameEnd + 2;
                                    continue;
                                }
                            }

                            i = end + 1;
                            continue;
                        }
                    }

                    return i;
                }

                return -1;
            }

            private static int FindLastTextEnd(string text)
            {
                int last = -1;
                for (int i = 0; i < text.Length;)
                {
                    if (text[i] == '{')
                    {
                        int end = text.IndexOf('}', i + 1);
                        if (end > i)
                        {
                            i = end + 1;
                            continue;
                        }
                    }

                    last = i + 1;
                    i++;
                }

                return last < 0 ? 0 : last;
            }

            private static int FindControlToken(string text, int start, string code, out int end)
            {
                for (int i = start; i < text.Length; i++)
                {
                    if (text[i] != '{')
                        continue;

                    end = text.IndexOf('}', i + 1);
                    if (end < 0)
                        break;

                    string token = text.Substring(i + 1, end - i - 1);
                    string tokenCode = token.Split(',')[0].Trim();
                    if (tokenCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                    {
                        end++;
                        return i;
                    }

                    i = end;
                }

                end = -1;
                return -1;
            }

            private static string GetStringIdentifier(string text)
            {
                int position = 0;
                while (true)
                {
                    int start = FindControlToken(text, position, "F81B", out int end);
                    if (start < 0)
                        return string.Empty;

                    string token = text.Substring(start + 1, end - start - 2);
                    string[] parts = token.Split(',').Select(x => x.Trim()).ToArray();
                    if (parts.Length >= 2)
                        return parts[1];

                    position = end;
                }
            }

            private static byte[] GetRawNamePrefix(byte[] record)
            {
                if (record == null || record.Length < 4 || record[0] != 0xF8 || record[1] != 0x12)
                    return null;

                for (int i = 2; i + 1 < record.Length; i++)
                {
                    if (record[i] == 0 && record[i + 1] == 0)
                        return record.Take(i + 2).ToArray();
                    if (record[i] == 0xFF && record[i + 1] == 0xFF)
                        return null;
                }

                return null;
            }

            private static byte[] EncodeNamePrefix(string name, RecordFormat recordFormat)
            {
                byte[] record = EncodeRecord("{F812}" + (name ?? string.Empty) + "\\0", recordFormat);
                return record.Take(record.Length - 2).ToArray();
            }

            private static byte[] CombineNamePrefix(byte[] namePrefix, byte[] body)
            {
                if (namePrefix == null || namePrefix.Length == 0)
                    return body;

                byte[] result = new byte[namePrefix.Length + body.Length];
                Buffer.BlockCopy(namePrefix, 0, result, 0, namePrefix.Length);
                Buffer.BlockCopy(body, 0, result, namePrefix.Length, body.Length);
                return result;
            }

            private static void SplitNamePrefix(string decodedText, out string name, out string body)
            {
                name = string.Empty;
                body = decodedText;

                const string nameMarker = "{F812}";
                if (!decodedText.StartsWith(nameMarker, StringComparison.OrdinalIgnoreCase))
                    return;

                int nullMarker = decodedText.IndexOf("\\0", nameMarker.Length, StringComparison.Ordinal);
                if (nullMarker < 0)
                    return;

                name = decodedText.Substring(nameMarker.Length, nullMarker - nameMarker.Length);
                body = decodedText.Substring(nullMarker + 2);
            }
        }

        public class MBMString
        {
            private readonly string prefix;
            private readonly string suffix;

            public int Index { get; }
            public string Identifier { get; }
            public string IdentifierOrIndex => string.IsNullOrEmpty(Identifier) ? Index.ToString(CultureInfo.InvariantCulture) : Identifier;
            public string OldText { get; }
            public string NewText { get; set; } = string.Empty;
            public bool HasEmbeddedNamePrefix => StartsWithNamePrefix(prefix);
            public bool HasChanges => !string.IsNullOrEmpty(NewText) && NewText != OldText;

            public MBMString(int index, string identifier, string prefix, string oldText, string suffix)
            {
                Index = index;
                Identifier = identifier ?? string.Empty;
                this.prefix = prefix ?? string.Empty;
                OldText = oldText ?? string.Empty;
                this.suffix = suffix ?? string.Empty;
            }

            public string GetText()
                => prefix + (HasChanges ? NewText : OldText) + suffix;

            public string GetText(string oldName, string newName)
            {
                string effectivePrefix = newName == null ? prefix : ReplaceLeadingNamePrefix(prefix, newName);
                return effectivePrefix + (HasChanges ? NewText : OldText) + suffix;
            }

            private static bool StartsWithNamePrefix(string value)
                => !string.IsNullOrEmpty(value)
                && value.StartsWith("{F812}", StringComparison.OrdinalIgnoreCase)
                && value.IndexOf("\\0", 6, StringComparison.Ordinal) >= 0;

            private static string ReplaceLeadingNamePrefix(string value, string newName)
            {
                if (!StartsWithNamePrefix(value))
                    return value;

                int nullMarker = value.IndexOf("\\0", 6, StringComparison.Ordinal);
                return "{F812}" + (newName ?? string.Empty) + "\\0" + value.Substring(nullMarker + 2);
            }
        }
    }
}
