using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditorLib.FileContainer
{
    /// <summary>
    /// Soul Hackers 1 EVENT.BIN, a directory followed by 0x800-aligned EVE blocks.
    /// </summary>
    public class EVENTBIN : IGameData
    {
        private const int DirectoryRecordSize = 0x14;
        private const int BlockAlignment = 0x800;

        private readonly byte[] prefix;
        private readonly List<byte[]> originalPadding = new List<byte[]>();
        private readonly List<int> originalRawSizes = new List<int>();

        public List<GameFile> SubFiles { get; } = new List<GameFile>();

        public EVENTBIN(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            Parse(data, out int dataStart, out List<string> catalogNames, out List<int> blockOffsets,
                out List<int> rawSizes);
            prefix = new byte[dataStart];
            Buffer.BlockCopy(data, 0, prefix, 0, dataStart);

            List<string> physicalNames = GetPhysicalNames(catalogNames, blockOffsets.Count);
            if (physicalNames.Count != blockOffsets.Count)
                throw new InvalidDataException("EVENT.BIN: directory contains more EVE names than blocks.");

            for (int i = 0; i < blockOffsets.Count; i++)
            {
                int start = blockOffsets[i];
                int rawSize = rawSizes[i];
                int allocationEnd = i + 1 < blockOffsets.Count ? blockOffsets[i + 1] : data.Length;
                byte[] raw = new byte[rawSize];
                Buffer.BlockCopy(data, start, raw, 0, rawSize);
                byte[] padding = new byte[allocationEnd - start - rawSize];
                Buffer.BlockCopy(data, start + rawSize, padding, 0, padding.Length);

                SubFiles.Add(new GameFile(physicalNames[i], new EVE(raw)));
                originalRawSizes.Add(rawSize);
                originalPadding.Add(padding);
            }
        }

        public static bool IsEventBin(byte[] data)
        {
            try
            {
                Parse(data, out _, out _, out _, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Parse(byte[] data, out int dataStart, out List<string> names,
            out List<int> blockOffsets, out List<int> rawSizes)
        {
            if (data == null || data.Length < DirectoryRecordSize)
                throw new InvalidDataException("EVENT.BIN: data is too small for a directory.");

            names = new List<string>();
            int position = 0;
            bool foundTerminator = false;
            while (position <= data.Length - DirectoryRecordSize)
            {
                ushort rawId = ReadUInt16(data, position);
                if (rawId == 0xFFFF)
                {
                    foundTerminator = true;
                    position += DirectoryRecordSize;
                    break;
                }

                string name = ReadName(data, position + 2, 16);
                if (string.IsNullOrEmpty(name)
                    || !name.EndsWith(".EVE", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("EVENT.BIN: invalid directory entry name.");

                names.Add(name);
                position += DirectoryRecordSize;
            }

            if (!foundTerminator)
                throw new InvalidDataException("EVENT.BIN: directory terminator was not found.");
            if (names.Count == 0)
                throw new InvalidDataException("EVENT.BIN: directory contains no EVE entries.");

            int catalogEnd = position - DirectoryRecordSize;
            dataStart = AlignUp(catalogEnd, BlockAlignment);
            while (dataStart < data.Length && !EVE.TryGetRawSize(data, dataStart, out _))
                dataStart += BlockAlignment;
            if (dataStart >= data.Length)
                throw new InvalidDataException("EVENT.BIN: no aligned EVE payload was found.");

            blockOffsets = new List<int>();
            rawSizes = new List<int>();
            int offset = dataStart;
            while (offset < data.Length)
            {
                if (!EVE.TryGetRawSize(data, offset, out int rawSize))
                    throw new InvalidDataException($"EVENT.BIN: invalid EVE payload at 0x{offset:X}.");
                int next = AlignUp(checked(offset + rawSize), BlockAlignment);
                if (next > data.Length)
                    throw new InvalidDataException($"EVENT.BIN: EVE payload at 0x{offset:X} extends past EOF.");

                blockOffsets.Add(offset);
                rawSizes.Add(rawSize);
                offset = next;
            }
            if (offset != data.Length)
                throw new InvalidDataException("EVENT.BIN: final EVE allocation is incomplete.");
        }

        private static List<string> GetPhysicalNames(List<string> catalogNames, int physicalCount)
        {
            List<string> referenced = catalogNames.Distinct(StringComparer.Ordinal).ToList();
            int unreferencedCount = physicalCount - referenced.Count;
            if (unreferencedCount < 0)
                throw new InvalidDataException(
                    "EVENT.BIN: archive has fewer physical EVEs than referenced names.");
            if (unreferencedCount == 0)
                return referenced;

            int insertion = referenced.FindIndex(x => x.StartsWith("DGST_", StringComparison.Ordinal));
            if (insertion < 0)
                insertion = referenced.Count;
            for (int i = 0; i < unreferencedCount; i++)
                referenced.Insert(insertion + i, $"UNREFERENCED_{i:00}.EVE");
            return referenced;
        }

        private static string ReadName(byte[] data, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && data[end] != 0)
                end++;

            for (int i = offset; i < end; i++)
                if (data[i] < 0x20 || data[i] > 0x7E)
                    throw new InvalidDataException("EVENT.BIN: directory name is not ASCII.");
            for (int i = end; i < offset + length; i++)
                if (data[i] != 0)
                    throw new InvalidDataException("EVENT.BIN: directory name padding is not zeroed.");

            return Encoding.ASCII.GetString(data, offset, end - offset);
        }

        private static ushort ReadUInt16(byte[] data, int offset)
            => (ushort)((data[offset] << 8) | data[offset + 1]);

        private static int AlignUp(int value, int alignment)
        {
            long aligned = ((long)value + alignment - 1) / alignment * alignment;
            if (aligned > int.MaxValue)
                throw new InvalidDataException("EVENT.BIN: aligned offset is too large.");
            return (int)aligned;
        }

        public FormatEnum Type => FormatEnum.EVENTBIN;

        public List<GameFile> GetSubFiles() => SubFiles;

        public int GetSize() => GetData().Length;

        public byte[] GetData()
        {
            using (var output = new MemoryStream(prefix.Length))
            {
                output.Write(prefix, 0, prefix.Length);
                for (int i = 0; i < SubFiles.Count; i++)
                {
                    GameFile file = SubFiles[i];
                    byte[] data;
                    try
                    {
                        data = file.GameData.GetData();
                    }
                    catch (Exception e)
                    {
                        throw new InvalidDataException(
                            $"EVENT.BIN: failed to save '{file.Name}': {e.Message}", e);
                    }

                    output.Write(data, 0, data.Length);
                    int paddingSize = AlignUp(data.Length, BlockAlignment) - data.Length;
                    byte[] padding = data.Length == originalRawSizes[i]
                        && originalPadding[i].Length == paddingSize
                        ? originalPadding[i]
                        : new byte[paddingSize];
                    if (padding.Length != 0)
                        output.Write(padding, 0, padding.Length);
                }

                byte[] result = output.ToArray();
                if (!IsEventBin(result))
                    throw new InvalidDataException("EVENT.BIN: rebuilt archive failed structural validation.");
                return result;
            }
        }

        public object Tag { get; set; }
    }
}
