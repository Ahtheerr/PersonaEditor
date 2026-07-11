using AuxiliaryLibraries.Tools;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditorLib.Text
{
    public class CatherineBMD : IGameData
    {
        public const uint MagicNumber = 0x12345678;
        private const int OffsetBase = 0x18;
        private readonly byte[] originalData;
        private readonly List<Record> records = new List<Record>();
        private readonly List<CatherineBMDEntry> entries = new List<CatherineBMDEntry>();
        private int tableOffset;

        public CatherineBMD(byte[] data)
        {
            originalData = data?.ToArray() ?? throw new ArgumentNullException(nameof(data));
            Read(originalData);
        }

        public IReadOnlyList<CatherineBMDEntry> Entries => entries;
        public bool IsLittleEndian { get; private set; }
        public FormatEnum Type => FormatEnum.CatherineBMD;
        public List<GameFile> SubFiles { get; } = new List<GameFile>();
        public int GetSize() => GetData().Length;

        public byte[] GetData()
        {
            if (entries.All(x => string.IsNullOrEmpty(x.NewText)))
                return originalData.ToArray();

            using MemoryStream ms = new MemoryStream();
            using BinaryWriter writer = IOTools.OpenWriteFile(ms, IsLittleEndian);

            ms.Write(originalData, 0, tableOffset);
            for (int i = 0; i < records.Count; i++)
                writer.Write(0);

            var recordStarts = new List<int>(records.Count);
            var slotTargets = new List<int>(entries.Count);

            foreach (Record record in records)
            {
                recordStarts.Add((int)ms.Position);
                writer.Write(record.Unknown);
                writer.Write(record.NameBytes);
                writer.Write((ushort)record.Slots.Count);
                writer.Write(record.Unknown2);

                long slotTable = ms.Position;
                for (int i = 0; i < record.Slots.Count; i++)
                    writer.Write(0);

                for (int i = 0; i < record.Slots.Count; i++)
                {
                    CatherineBMDEntry slot = record.Slots[i];
                    int target = (int)ms.Position;
                    slotTargets.Add(target);

                    long current = ms.Position;
                    ms.Position = slotTable + i * 4;
                    writer.Write(target - OffsetBase);
                    ms.Position = current;

                    byte[] raw = slot.GetData();
                    writer.Write(raw);
                }
            }

            int dataEnd = (int)ms.Position;
            byte[] footer = CreatePointersDiffSection(GetPointerPositions(recordStarts, slotTargets));
            writer.Write(footer);

            ms.Position = tableOffset;
            foreach (int recordStart in recordStarts)
                writer.Write(recordStart - OffsetBase);

            ms.Position = 0x0C;
            writer.Write((uint)ms.Length);
            writer.Write((uint)dataEnd);
            writer.Write((uint)footer.Length);
            ms.Position = 0x20;
            writer.Write((uint)(dataEnd - OffsetBase));

            return ms.ToArray();
        }

        public string[] ExportText(string fileName, bool removeSplit)
        {
            return entries.Select((entry, index) =>
                $"{fileName}\t{index}\t{entry.Name}\t{EscapeText(entry.OldText, removeSplit)}\t").ToArray();
        }

        public void ImportTextByIndex(IEnumerable<(int Index, string Text)> importedText, Dictionary<char, int> charWidth = null, int width = 0)
        {
            if (importedText == null)
                return;

            foreach (var item in importedText)
            {
                if (item.Index < 0 || item.Index >= entries.Count || string.IsNullOrEmpty(item.Text))
                    continue;

                string text = width > 0 && charWidth != null ? item.Text.SplitByWidthOrImportedRaw(charWidth, width) : item.Text.NormalizeImportedText();
                entries[item.Index].NewText = text;
            }
        }

        private void Read(byte[] data)
        {
            if (data.Length < 0x28)
                throw new Exception("Catherine BMD: file too small");

            IsLittleEndian = BinaryPrimitives.ReadUInt32LittleEndian(data) == MagicNumber;
            if (ReadUInt32(data, 0) != MagicNumber)
                throw new Exception("Catherine BMD: wrong magic number");
            if (ReadUInt32(data, 0x0C) != data.Length)
                throw new Exception("Catherine BMD: header file size mismatch");

            int dataEnd = checked((int)ReadUInt32(data, 0x10));
            int footerSize = checked((int)ReadUInt32(data, 0x14));
            tableOffset = OffsetBase + checked((int)ReadUInt32(data, 0x18));
            int count = checked((int)ReadUInt32(data, 0x1C));

            if (dataEnd < 0 || footerSize < 0 || dataEnd + footerSize != data.Length)
                throw new Exception("Catherine BMD: data/footer size mismatch");
            if (tableOffset < 0 || tableOffset + count * 4 > data.Length)
                throw new Exception("Catherine BMD: invalid offset table");

            int[] offsets = new int[count];
            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = OffsetBase + checked((int)ReadUInt32(data, tableOffset + i * 4));

            for (int i = 0; i < offsets.Length; i++)
                ReadRecord(data, i, offsets[i], i + 1 < offsets.Length ? offsets[i + 1] : dataEnd);
        }

        private void ReadRecord(byte[] data, int recordIndex, int start, int end)
        {
            if (start < 0 || start + 0x28 > end || end > data.Length)
                throw new Exception("Catherine BMD: invalid record offset");

            ushort slotCount = ReadUInt16(data, start + 0x24);
            if (start + 0x28 + slotCount * 4 > end)
                throw new Exception("Catherine BMD: invalid slot table");

            var record = new Record
            {
                Unknown = ReadUInt32(data, start),
                NameBytes = data.Skip(start + 4).Take(0x20).ToArray(),
                Unknown2 = ReadInt16(data, start + 0x26)
            };

            string name = ReadAsciiName(record.NameBytes);
            int[] slotStarts = new int[slotCount];
            for (int i = 0; i < slotStarts.Length; i++)
            {
                slotStarts[i] = OffsetBase + checked((int)ReadUInt32(data, start + 0x28 + i * 4));
                if (slotStarts[i] < start || slotStarts[i] > end)
                    throw new Exception("Catherine BMD: invalid text offset");
            }

            for (int i = 0; i < slotStarts.Length; i++)
            {
                int textEnd = i + 1 < slotStarts.Length ? slotStarts[i + 1] : end;
                if (textEnd < slotStarts[i] || textEnd > end)
                    throw new Exception("Catherine BMD: invalid text range");

                string entryName = slotStarts.Length == 1 ? name : $"{name}:{i}";
                var entry = new CatherineBMDEntry(entries.Count, recordIndex, i, entryName, data.Skip(slotStarts[i]).Take(textEnd - slotStarts[i]).ToArray(), IsLittleEndian);
                record.Slots.Add(entry);
                entries.Add(entry);
            }

            records.Add(record);
        }

        private IEnumerable<int> GetPointerPositions(List<int> recordStarts, List<int> slotTargets)
        {
            yield return 0x20;

            for (int i = 0; i < records.Count; i++)
                if (i != 1)
                    yield return tableOffset + i * 4;

            if (recordStarts.Count > 0)
            {
                yield return recordStarts[0];
                yield return recordStarts[0] + 4;
            }

            foreach (int target in slotTargets)
                yield return target + 4;
        }

        private static byte[] CreatePointersDiffSection(IEnumerable<int> pointerPositions)
        {
            var pointers = pointerPositions.OrderBy(x => x).ToArray();
            List<byte> encodedDiffs = new List<byte>();

            for (int i = 0; i < pointers.Length; i++)
            {
                int consecutive = 0;
                for (int j = i; j > 0 && j < pointers.Length; j++)
                {
                    int diff = pointers[j] - pointers[j - 1];
                    if (diff == sizeof(uint))
                        consecutive++;
                    else
                        break;
                }

                if (consecutive >= 2)
                {
                    consecutive = consecutive > 33 ? 33 : consecutive;
                    encodedDiffs.Add((byte)(((consecutive - 2) << 3) | 0b111));
                    i += consecutive - 1;
                    continue;
                }

                int prevPtr = i > 0 ? pointers[i - 1] : 0x20;
                int diffWords = (pointers[i] - prevPtr) / 4;
                if (diffWords < 128)
                    encodedDiffs.Add((byte)(diffWords << 1));
                else if (diffWords < 16384)
                {
                    int encoded = (diffWords << 2) | 0b01;
                    encodedDiffs.Add((byte)(encoded & 0xFF));
                    encodedDiffs.Add((byte)(encoded >> 8));
                }
                else if (diffWords < 2097152)
                {
                    int encoded = (diffWords << 3) | 0b011;
                    encodedDiffs.Add((byte)(encoded & 0xFF));
                    encodedDiffs.Add((byte)((encoded >> 8) & 0xFF));
                    encodedDiffs.Add((byte)(encoded >> 16));
                }
                else
                {
                    throw new FormatException("Pointer difference too big");
                }
            }

            return encodedDiffs.ToArray();
        }

        private static string EscapeText(string text, bool removeSplit)
        {
            text ??= string.Empty;
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return removeSplit ? text.Replace("\n", " ") : text.Replace("\n", "\\n");
        }

        private static string ReadAsciiName(byte[] data)
        {
            int length = Array.IndexOf(data, (byte)0);
            if (length < 0)
                length = data.Length;
            return Encoding.ASCII.GetString(data, 0, length);
        }

        private uint ReadUInt32(byte[] data, int offset)
            => IsLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

        private ushort ReadUInt16(byte[] data, int offset)
            => IsLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

        private short ReadInt16(byte[] data, int offset)
            => IsLittleEndian ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2)) : BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));

        private class Record
        {
            public uint Unknown;
            public byte[] NameBytes;
            public short Unknown2;
            public List<CatherineBMDEntry> Slots { get; } = new List<CatherineBMDEntry>();
        }

        public class CatherineBMDEntry
        {
            private readonly byte[] raw;
            private readonly bool isLittleEndian;
            private readonly bool leadingOddNull;
            private readonly ushort[] prefixUnits;
            private readonly ushort[] suffixUnits;

            public int Index { get; }
            public int RecordIndex { get; }
            public int SlotIndex { get; }
            public string Name { get; }
            public string OldText { get; }
            public string NewText { get; set; } = string.Empty;

            public CatherineBMDEntry(int index, int recordIndex, int slotIndex, string name, byte[] raw, bool isLittleEndian)
            {
                this.raw = raw;
                this.isLittleEndian = isLittleEndian;
                Index = index;
                RecordIndex = recordIndex;
                SlotIndex = slotIndex;
                Name = name;

                ushort[] units = GetUnits(raw, isLittleEndian, out leadingOddNull);
                int bodyEnd = GetBodyEnd(units);
                int bodyStart = GetBodyStart(units);

                prefixUnits = units.Take(bodyStart).ToArray();
                suffixUnits = units.Skip(bodyEnd).ToArray();
                OldText = DecodeVisibleText(units, bodyStart, bodyEnd, isLittleEndian);
            }

            public byte[] GetData()
            {
                if (string.IsNullOrEmpty(NewText))
                    return raw.ToArray();

                var units = new List<ushort>(prefixUnits);
                units.AddRange(EncodeText(NewText, isLittleEndian));
                if (suffixUnits.Length == 0 || suffixUnits[0] == 0xD821)
                    units.Add(0);
                units.AddRange(suffixUnits);

                using MemoryStream ms = new MemoryStream();
                using BinaryWriter writer = IOTools.OpenWriteFile(ms, isLittleEndian);
                if (leadingOddNull)
                    writer.Write((byte)0);
                foreach (ushort unit in units)
                    writer.Write(unit);
                return ms.ToArray();
            }

            private static ushort[] GetUnits(byte[] raw, bool isLittleEndian, out bool leadingOddNull)
            {
                leadingOddNull = raw.Length % 2 == 1 && raw.Length > 0 && raw[0] == 0;
                int start = leadingOddNull ? 1 : 0;
                int count = (raw.Length - start) / 2;
                ushort[] units = new ushort[count];
                for (int i = 0; i < count; i++)
                    units[i] = isLittleEndian
                        ? BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(start + i * 2, 2))
                        : BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(start + i * 2, 2));
                return units;
            }

            private static int GetBodyEnd(ushort[] units)
            {
                int end = Array.IndexOf(units, (ushort)0xD821);
                if (end >= 0)
                    return end;

                end = units.Length;
                while (end > 0 && units[end - 1] == 0)
                    end--;
                return end;
            }

            private static int GetBodyStart(ushort[] units)
            {
                int i = 0;
                if (units.Length > 0 && IsSurrogate(units[0]))
                {
                    int sep = FindSeparator(units, 1, Math.Min(units.Length, 5));
                    i = sep >= 0 ? sep + 1 : 1;
                }

                while (i < units.Length && IsSurrogate(units[i]))
                {
                    int sep = FindSeparator(units, i + 1, Math.Min(units.Length, i + 8));
                    i = sep >= 0 ? sep + 1 : i + 1;
                }

                return i;
            }

            private static string DecodeVisibleText(ushort[] units, int start, int end, bool isLittleEndian)
            {
                var visible = new List<ushort>();
                for (int i = start; i < end;)
                {
                    ushort unit = units[i];
                    if (IsSurrogate(unit))
                    {
                        i += i + 1 < end && (units[i + 1] <= 0x1F || units[i + 1] == 0xFFFF) ? 2 : 1;
                        continue;
                    }

                    if (unit != 0 && (unit <= 0x1F || unit == 0xFFFF))
                    {
                        i++;
                        continue;
                    }

                    visible.Add(!isLittleEndian && unit == 0xFFE3 ? (ushort)' ' : unit);
                    i++;
                }

                byte[] bytes = new byte[visible.Count * 2];
                for (int i = 0; i < visible.Count; i++)
                {
                    bytes[i * 2] = (byte)(visible[i] & 0xFF);
                    bytes[i * 2 + 1] = (byte)(visible[i] >> 8);
                }

                return Encoding.Unicode.GetString(bytes).Replace("\0", "\n");
            }

            private static IEnumerable<ushort> EncodeText(string text, bool isLittleEndian)
            {
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                foreach (char c in text)
                    yield return c == '\n' ? (ushort)0 : !isLittleEndian && c == ' ' ? (ushort)0xFFE3 : c;
            }

            private static bool IsSurrogate(ushort unit) => unit >= 0xD800 && unit <= 0xDFFF;

            private static int FindSeparator(ushort[] units, int start, int end)
            {
                for (int i = start; i < end; i++)
                    if (units[i] == 1)
                        return i;
                return -1;
            }
        }
    }
}
