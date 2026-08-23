using System;
using System.Collections.Generic;
using System.IO;
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

        private readonly byte[] directoryAndPadding;

        public List<GameFile> SubFiles { get; } = new List<GameFile>();

        public EVENTBIN(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            Parse(data, out int dataStart, out List<string> names, out List<int> blockOffsets);
            directoryAndPadding = new byte[dataStart];
            Buffer.BlockCopy(data, 0, directoryAndPadding, 0, dataStart);

            var uniqueNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
                if (seenNames.Add(name))
                    uniqueNames.Add(name);

            if (uniqueNames.Count > blockOffsets.Count)
                throw new InvalidDataException("EVENT.BIN: directory contains more EVE names than blocks.");

            for (int i = 0; i < blockOffsets.Count; i++)
            {
                int start = blockOffsets[i];
                int end = i + 1 < blockOffsets.Count ? blockOffsets[i + 1] : data.Length;
                int size = end - start;
                byte[] block = new byte[size];
                Buffer.BlockCopy(data, start, block, 0, size);

                string name = i < uniqueNames.Count
                    ? uniqueNames[i]
                    : $"UNREFERENCED_{i - uniqueNames.Count:00}.EVE";
                SubFiles.Add(new GameFile(name, new EVE(block)));
            }
        }

        public static bool IsEventBin(byte[] data)
        {
            try
            {
                Parse(data, out _, out _, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Parse(byte[] data, out int dataStart, out List<string> names,
            out List<int> blockOffsets)
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

                string name = ReadName(data, position + 2, 12);
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

            dataStart = AlignUp(position, BlockAlignment);
            if (dataStart > data.Length)
                throw new InvalidDataException("EVENT.BIN: directory extends past the end of the file.");

            blockOffsets = new List<int>();
            for (int offset = dataStart; offset <= data.Length - 8; offset += BlockAlignment)
                if (EVE.TryReadHeader(data, offset, out _, out _, out _, out _))
                    blockOffsets.Add(offset);

            if (blockOffsets.Count == 0)
                throw new InvalidDataException("EVENT.BIN: no EVE blocks were found.");
        }

        private static string ReadName(byte[] data, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && data[end] != 0)
                end++;

            for (int i = offset; i < end; i++)
                if (!((data[i] >= 'A' && data[i] <= 'Z')
                    || (data[i] >= '0' && data[i] <= '9')
                    || data[i] == '_' || data[i] == '.'))
                    throw new InvalidDataException("EVENT.BIN: directory name is not ASCII.");

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
            using (var output = new MemoryStream(directoryAndPadding.Length))
            {
                output.Write(directoryAndPadding, 0, directoryAndPadding.Length);
                foreach (GameFile file in SubFiles)
                {
                    byte[] data = file.GameData.GetData();
                    output.Write(data, 0, data.Length);
                    int padding = AlignUp(data.Length, BlockAlignment) - data.Length;
                    if (padding != 0)
                        output.Write(new byte[padding], 0, padding);
                }
                return output.ToArray();
            }
        }

        public object Tag { get; set; }
    }
}
