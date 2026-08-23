using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PersonaEditorLib.FileContainer
{
    /// <summary>
    /// Unreal BmdAssetPlugin package whose mBuf byte array contains one Atlus BMD.
    /// </summary>
    public class UAssetBMD : IGameData
    {
        private const int ArraySizeOffsetFromBmd = 0x15;
        private const int ElementCountOffsetFromBmd = 0x04;

        private readonly byte[] prefix;
        private readonly byte[] suffix;
        private readonly int arraySizeOffset;
        private readonly int elementCountOffset;
        private readonly int serialSizeOffset;

        public UAssetBMD(byte[] data, string name)
        {
            LocateBmd(data, out int bmdOffset, out int bmdSize, out int cookedSerialSizeOffset);

            prefix = data.AsSpan(0, bmdOffset).ToArray();
            suffix = data.AsSpan(bmdOffset + bmdSize).ToArray();
            arraySizeOffset = bmdOffset - ArraySizeOffsetFromBmd;
            elementCountOffset = bmdOffset - ElementCountOffsetFromBmd;
            serialSizeOffset = cookedSerialSizeOffset;

            byte[] bmdData = data.AsSpan(bmdOffset, bmdSize).ToArray();
            string bmdName = Path.GetFileNameWithoutExtension(name) + ".BMD";
            SubFiles.Add(new GameFile(bmdName, new Text.BMD(bmdData) { IsReload = true }));
        }

        public static bool IsUAssetBmd(byte[] data)
        {
            try
            {
                LocateBmd(data, out _, out _, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void LocateBmd(byte[] data, out int bmdOffset, out int bmdSize, out int serialSizeOffset)
        {
            bmdOffset = -1;
            bmdSize = 0;
            serialSizeOffset = -1;
            if (data == null || data.Length < 0x40)
                throw new InvalidDataException("UASSET BMD: data is too small.");
            if (!ContainsAscii(data, "/Script/BmdAssetPlugin") || !ContainsAscii(data, "mBuf"))
                throw new InvalidDataException("UASSET BMD: BmdAssetPlugin metadata was not found.");

            for (int magicOffset = 8; magicOffset <= data.Length - 4; magicOffset++)
            {
                bool littleEndian = HasMagic(data, magicOffset, "MSG1");
                bool bigEndian = HasMagic(data, magicOffset, "1GSM");
                if (!littleEndian && !bigEndian)
                    continue;

                int candidateOffset = magicOffset - 8;
                if (candidateOffset < ArraySizeOffsetFromBmd)
                    continue;

                int version = littleEndian
                    ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(candidateOffset, 4))
                    : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(candidateOffset, 4));
                int candidateSize = littleEndian
                    ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(candidateOffset + 4, 4))
                    : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(candidateOffset + 4, 4));
                if ((version != 7 && version != 0x07000000)
                    || candidateSize < 0x20 || candidateSize > data.Length - candidateOffset)
                    continue;

                int arraySizeOffset = candidateOffset - ArraySizeOffsetFromBmd;
                int elementCountOffset = candidateOffset - ElementCountOffsetFromBmd;
                if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(arraySizeOffset, 4)) != candidateSize + 4
                    || BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(elementCountOffset, 4)) != candidateSize)
                    continue;

                int exportMapOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x2C, 4)));
                int candidateSerialSizeOffset = checked(exportMapOffset + sizeof(long));
                if (exportMapOffset < 0x40 || candidateSerialSizeOffset > candidateOffset - sizeof(long)
                    || BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(candidateSerialSizeOffset, sizeof(long)))
                        != (ulong)candidateSize + 0x31)
                    continue;

                bmdOffset = candidateOffset;
                bmdSize = candidateSize;
                serialSizeOffset = candidateSerialSizeOffset;
                return;
            }

            throw new InvalidDataException("UASSET BMD: a valid mBuf BMD payload was not found.");
        }

        private static bool ContainsAscii(byte[] data, string value)
        {
            byte[] pattern = Encoding.ASCII.GetBytes(value);
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j])
                    j++;
                if (j == pattern.Length)
                    return true;
            }
            return false;
        }

        private static bool HasMagic(byte[] data, int offset, string magic)
        {
            for (int i = 0; i < magic.Length; i++)
                if (data[offset + i] != magic[i])
                    return false;
            return true;
        }

        public FormatEnum Type => FormatEnum.UASSETBMD;
        public List<GameFile> SubFiles { get; } = new List<GameFile>();
        public int GetSize() => GetData().Length;

        public byte[] GetData()
        {
            if (SubFiles.Count != 1 || SubFiles[0].GameData is not Text.BMD)
                throw new InvalidDataException("UASSET BMD: exactly one BMD child is required.");

            byte[] bmd = SubFiles[0].GameData.GetData();
            byte[] rebuiltPrefix = (byte[])prefix.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(rebuiltPrefix.AsSpan(arraySizeOffset, 4), checked(bmd.Length + 4));
            BinaryPrimitives.WriteInt32LittleEndian(rebuiltPrefix.AsSpan(elementCountOffset, 4), bmd.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(rebuiltPrefix.AsSpan(serialSizeOffset, sizeof(long)),
                checked((ulong)bmd.Length + 0x31));

            using MemoryStream output = new MemoryStream(checked(rebuiltPrefix.Length + bmd.Length + suffix.Length));
            output.Write(rebuiltPrefix, 0, rebuiltPrefix.Length);
            output.Write(bmd, 0, bmd.Length);
            output.Write(suffix, 0, suffix.Length);
            return output.ToArray();
        }
    }
}
