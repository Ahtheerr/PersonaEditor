using AuxiliaryLibraries.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PersonaEditorLib.Sprite
{
    public sealed class STEX : IGameData, IImage
    {
        private static readonly Dictionary<uint, int> FormatMap = new Dictionary<uint, int>
        {
            [0x14016752] = 0,  // RGBA8888
            [0x80336752] = 4,  // RGBA4444
            [0x80346752] = 2,  // RGBA5551
            [0x14016754] = 1,  // RGB888
            [0x83636754] = 3,  // RGB565
            [0x14016756] = 8,  // A8
            [0x67616756] = 11, // A4
            [0x14016757] = 7,  // L8
            [0x67616757] = 10, // L4
            [0x14016758] = 5,  // LA88
            [0x67606758] = 9,  // LA44
            [0x0000675A] = 12, // ETC1
            [0x0000675B] = 13, // ETC1A4
            [0x1401675A] = 12, // ETC1
            [0x1401675B] = 13  // ETC1A4
        };

        private byte[] originalData;
        private byte[] stexData;
        private readonly bool isCompressed;
        private readonly int width;
        private readonly int height;
        private readonly int dataOffset;
        private readonly int dataSize;
        private readonly int ctrFormat;
        private Bitmap bitmap;

        public STEX(byte[] data)
        {
            originalData = CopyBytes(data ?? throw new ArgumentNullException(nameof(data)));
            isCompressed = !HasMagic(originalData, 0, "STEX");
            stexData = GetStexData(originalData);
            if (stexData.Length < 0x24)
                throw new InvalidDataException("STEX: file is too small.");

            using (var reader = new BinaryReader(new MemoryStream(stexData), Encoding.ASCII))
            {
                string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (magic != "STEX")
                    throw new InvalidDataException("STEX: wrong magic.");

                uint zero = reader.ReadUInt32();
                uint constant = reader.ReadUInt32();
                width = reader.ReadInt32();
                height = reader.ReadInt32();
                uint type = reader.ReadUInt32();
                uint imageFormat = reader.ReadUInt32();
                dataSize = reader.ReadInt32();

                if (zero != 0 || constant != 0xDE1)
                    throw new InvalidDataException("STEX: invalid header.");
                if (width <= 0 || height <= 0 || dataSize <= 0)
                    throw new InvalidDataException("STEX: invalid dimensions or data size.");

                uint format = type << 16 | imageFormat;
                if (!FormatMap.TryGetValue(format, out ctrFormat))
                    throw new NotSupportedException($"STEX: unsupported texture format 0x{format:X8}.");

                dataOffset = reader.ReadInt32();
                if (dataOffset < 0 || dataSize > stexData.Length - dataOffset)
                    throw new InvalidDataException("STEX: texture data points outside the file.");
            }
        }

        public FormatEnum Type => FormatEnum.STEX;

        public List<GameFile> SubFiles { get; } = new List<GameFile>();

        public int GetSize() => originalData.Length;

        public byte[] GetData()
        {
            byte[] copy = new byte[originalData.Length];
            Buffer.BlockCopy(originalData, 0, copy, 0, copy.Length);
            return copy;
        }

        public Bitmap GetBitmap()
        {
            if (bitmap == null)
            {
                byte[] texture = new byte[dataSize];
                Buffer.BlockCopy(stexData, dataOffset, texture, 0, texture.Length);
                bitmap = CTPK.ReadCtrBitmap(texture, width, height, ctrFormat);
            }

            return bitmap;
        }

        public void SetBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                return;
            if (isCompressed)
                throw new NotSupportedException("Compressed STEX encoding is not supported.");
            if (bitmap.Width != width || bitmap.Height != height)
                throw new Exception($"STEX: image must be {width}x{height}");

            Bitmap source = bitmap.PixelFormat == PixelFormats.Bgra32 ? bitmap : bitmap.ConvertTo(PixelFormats.Bgra32, null);
            byte[] texture = CTPK.WriteCtrBitmap(source, ctrFormat);
            if (texture.Length != dataSize)
                throw new InvalidDataException("STEX: encoded texture size does not match the original payload.");

            byte[] editedData = CopyBytes(stexData);
            Buffer.BlockCopy(texture, 0, editedData, dataOffset, texture.Length);
            stexData = editedData;
            originalData = editedData;
            this.bitmap = source;
        }

        private static byte[] GetStexData(byte[] data)
        {
            if (HasMagic(data, 0, "STEX"))
                return CopyBytes(data);

            if (NintendoCompression.TryDecompress(data, out byte[] decompressed) && HasMagic(decompressed, 0, "STEX"))
                return decompressed;

            throw new InvalidDataException("STEX: file is neither plain STEX nor compressed STEX.");
        }

        internal static bool IsStex(byte[] data)
        {
            if (HasMagic(data, 0, "STEX"))
                return true;

            return NintendoCompression.TryDecompress(data, out byte[] decompressed) && HasMagic(decompressed, 0, "STEX");
        }

        private static bool HasMagic(byte[] data, int offset, string magic)
            => data != null
            && data.Length >= offset + magic.Length
            && Encoding.ASCII.GetString(data, offset, magic.Length) == magic;

        private static byte[] CopyBytes(byte[] data)
        {
            byte[] copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, copy.Length);
            return copy;
        }
    }
}
