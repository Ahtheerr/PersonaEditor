using AuxiliaryLibraries.Media;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace PersonaEditorLib.Sprite
{
    public sealed class DMPBM : IGameData, IImage
    {
        private readonly byte[] compressedData;
        private readonly byte[] decompressedData;
        private Bitmap bitmap;
        private readonly byte pixelFormat;
        private readonly int width;
        private readonly int height;

        private static readonly int[] Tiled3dsOrder =
        {
             0,  1,  8,  9,  2,  3, 10, 11,
            16, 17, 24, 25, 18, 19, 26, 27,
             4,  5, 12, 13,  6,  7, 14, 15,
            20, 21, 28, 29, 22, 23, 30, 31,
            32, 33, 40, 41, 34, 35, 42, 43,
            48, 49, 56, 57, 50, 51, 58, 59,
            36, 37, 44, 45, 38, 39, 46, 47,
            52, 53, 60, 61, 54, 55, 62, 63
        };

        public DMPBM(byte[] data)
        {
            compressedData = data ?? throw new ArgumentNullException(nameof(data));
            decompressedData = NintendoCompression.Decompress(data);

            using (var reader = new BinaryReader(new MemoryStream(decompressedData)))
            {
                string magic = Encoding.ASCII.GetString(reader.ReadBytes(5));
                if (magic != "DMPBM")
                    throw new InvalidDataException("DMPBM: wrong magic.");

                pixelFormat = reader.ReadByte();
                width = checked((int)reader.ReadUInt32());
                height = checked((int)reader.ReadUInt32());
            }
        }

        public FormatEnum Type => FormatEnum.CMP;

        public List<GameFile> SubFiles { get; } = new List<GameFile>();

        public int GetSize() => compressedData.Length;

        public byte[] GetData()
        {
            byte[] data = new byte[compressedData.Length];
            Buffer.BlockCopy(compressedData, 0, data, 0, data.Length);
            return data;
        }

        public Bitmap GetBitmap()
        {
            if (bitmap == null)
                bitmap = DecodeBitmap();

            return bitmap;
        }

        public void SetBitmap(Bitmap bitmap)
        {
            throw new NotSupportedException("DMPBM CMP encoding is not supported.");
        }

        private Bitmap DecodeBitmap()
        {
            ReadOnlySpan<byte> data = decompressedData;
            int offset = 14;
            Color[] palette = null;

            if (pixelFormat == 0x04)
            {
                palette = ReadPalette(data.Slice(offset, 256 * 2));
                offset += 256 * 2;
            }

            int bytesPerPixel = pixelFormat == 0x03 ? 4 : pixelFormat == 0x00 || pixelFormat == 0x04 ? 1 : 2;
            int pixelCount = checked(width * height);
            byte[] bgra = new byte[checked(pixelCount * 4)];

            for (int sourceIndex = 0; sourceIndex < pixelCount; sourceIndex++)
            {
                GetPixelCoordinates3ds(sourceIndex, out int x, out int y);
                if (x >= width || y >= height)
                    continue;

                int dest = (((height - 1 - y) * width) + x) * 4;
                ReadPixel(data.Slice(offset + sourceIndex * bytesPerPixel), palette, bgra, dest);
            }

            return new Bitmap(width, height, PixelFormats.Bgra32, bgra, null);
        }

        private void ReadPixel(ReadOnlySpan<byte> pixel, Color[] palette, byte[] bgra, int dest)
        {
            switch (pixelFormat)
            {
                case 0x00:
                    bgra[dest + 3] = pixel[0];
                    break;
                case 0x01:
                    WriteRgba5551((ushort)(pixel[0] | pixel[1] << 8), bgra, dest);
                    break;
                case 0x02:
                    WriteRgba4444((ushort)(pixel[0] | pixel[1] << 8), bgra, dest);
                    break;
                case 0x03:
                    bgra[dest] = pixel[2];
                    bgra[dest + 1] = pixel[1];
                    bgra[dest + 2] = pixel[0];
                    bgra[dest + 3] = pixel[3];
                    break;
                case 0x04:
                    Color color = palette[pixel[0]];
                    bgra[dest] = color.B;
                    bgra[dest + 1] = color.G;
                    bgra[dest + 2] = color.R;
                    bgra[dest + 3] = color.A;
                    break;
                default:
                    throw new NotSupportedException($"DMPBM pixel format 0x{pixelFormat:X2} is not supported.");
            }
        }

        private static Color[] ReadPalette(ReadOnlySpan<byte> data)
        {
            Color[] palette = new Color[256];
            for (int i = 0; i < palette.Length; i++)
            {
                ushort value = (ushort)(data[i * 2] | data[i * 2 + 1] << 8);
                int r = Scale5(value & 0x1F);
                int g = Scale5((value >> 5) & 0x1F);
                int b = Scale5((value >> 10) & 0x1F);
                palette[i] = Color.FromArgb(255, r, g, b);
            }

            return palette;
        }

        private static void WriteRgba5551(ushort value, byte[] bgra, int offset)
        {
            bgra[offset] = (byte)Scale5((value >> 1) & 0x1F);
            bgra[offset + 1] = (byte)Scale5((value >> 6) & 0x1F);
            bgra[offset + 2] = (byte)Scale5((value >> 11) & 0x1F);
            bgra[offset + 3] = (value & 1) != 0 ? (byte)255 : (byte)0;
        }

        private static void WriteRgba4444(ushort value, byte[] bgra, int offset)
        {
            bgra[offset] = (byte)Scale4((value >> 4) & 0xF);
            bgra[offset + 1] = (byte)Scale4((value >> 8) & 0xF);
            bgra[offset + 2] = (byte)Scale4((value >> 12) & 0xF);
            bgra[offset + 3] = (byte)Scale4(value & 0xF);
        }

        private void GetPixelCoordinates3ds(int sourceIndex, out int x, out int y)
        {
            int globalX = sourceIndex / 64 * 8;
            int globalY = globalX / width * 8;
            globalX %= width;

            int inTilePixel = sourceIndex % 64;
            x = globalX + Tiled3dsOrder[inTilePixel] % 8;
            y = globalY + Tiled3dsOrder[inTilePixel] / 8;
        }

        private static int Scale4(int value) => value << 4 | value;

        private static int Scale5(int value) => value << 3 | value >> 2;
    }
}
