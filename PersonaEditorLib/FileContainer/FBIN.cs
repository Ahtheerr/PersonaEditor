using PersonaEditorLib.Other;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PersonaEditorLib.FileContainer
{
    /// <summary>
    /// An FBIN is an unnamed, little-endian list of size-delimited files.
    /// </summary>
    public class FBIN : IGameData
    {
        private const int FixedHeaderSize = 0x0C;

        private readonly byte[] header;
        private readonly int entryCount;

        public FBIN(byte[] data, string name)
        {
            Parse(data, out int headerSize, out int[] sizes);

            entryCount = sizes.Length;
            header = data.AsSpan(0, headerSize).ToArray();

            int position = headerSize;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte[] entryData = data.AsSpan(position, sizes[i]).ToArray();
                position += sizes[i];

                string entryName = GetEntryName(name, entryData, i);
                GameFile entry = GameFormatHelper.OpenFile(entryName, entryData)
                    ?? new GameFile(entryName, new DAT(entryData));
                entry.Tag = i;
                SubFiles.Add(entry);
            }
        }

        public static bool IsFbin(byte[] data)
        {
            try
            {
                Parse(data, out _, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Parse(byte[] data, out int headerSize, out int[] sizes)
        {
            if (data == null || data.Length < 0x10)
                throw new InvalidDataException("FBIN: data is too small.");
            if (Encoding.ASCII.GetString(data, 0, 4) != "FBIN")
                throw new InvalidDataException("FBIN: wrong magic number.");

            int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)));
            headerSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
            long minimumHeaderSize = FixedHeaderSize + (long)count * sizeof(int);
            if (count <= 0 || minimumHeaderSize > data.Length)
                throw new InvalidDataException("FBIN: invalid entry count.");
            if (headerSize < minimumHeaderSize || headerSize > data.Length || (headerSize & 0x0F) != 0)
                throw new InvalidDataException("FBIN: invalid header size.");

            sizes = new int[count];
            long end = headerSize;
            for (int i = 0; i < sizes.Length; i++)
            {
                sizes[i] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(FixedHeaderSize + i * sizeof(int), 4)));
                if (sizes[i] <= 0)
                    throw new InvalidDataException("FBIN: entry size must be positive.");
                end += sizes[i];
                if (end > data.Length)
                    throw new InvalidDataException("FBIN: entry extends past the end of the file.");
            }

            if (end != data.Length)
                throw new InvalidDataException("FBIN: entry sizes do not cover the file.");
        }

        private static string GetEntryName(string containerName, byte[] data, int index)
        {
            string baseName = Path.GetFileNameWithoutExtension(containerName);
            string extension;
            FormatEnum format = GameFormatHelper.GetFormat(data);
            if (format == FormatEnum.MBM)
                extension = ".MBM";
            else if (format == FormatEnum.BF)
                extension = ".BF";
            else if (data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "T2B1")
                extension = ".T2B1";
            else
                extension = ".DAT";

            return $"{baseName}({index:D2}){extension}";
        }

        public FormatEnum Type => FormatEnum.FBIN;
        public List<GameFile> SubFiles { get; } = new List<GameFile>();
        public int GetSize() => GetData().Length;

        public byte[] GetData()
        {
            if (SubFiles.Count != entryCount)
                throw new InvalidDataException("FBIN: changing the number of entries is not supported.");

            byte[][] entries = new byte[entryCount][];
            long totalSize = header.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = SubFiles[i].GameData.GetData();
                if (entries[i].Length == 0)
                    throw new InvalidDataException("FBIN: entries cannot be empty.");
                totalSize += entries[i].Length;
            }
            if (totalSize > int.MaxValue)
                throw new InvalidDataException("FBIN: rebuilt file is too large.");

            byte[] rebuiltHeader = (byte[])header.Clone();
            for (int i = 0; i < entries.Length; i++)
                BinaryPrimitives.WriteInt32LittleEndian(
                    rebuiltHeader.AsSpan(FixedHeaderSize + i * sizeof(int), 4), entries[i].Length);

            using MemoryStream output = new MemoryStream((int)totalSize);
            output.Write(rebuiltHeader, 0, rebuiltHeader.Length);
            foreach (byte[] entry in entries)
                output.Write(entry, 0, entry.Length);
            return output.ToArray();
        }
    }
}
