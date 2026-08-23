using AuxiliaryLibraries.Media;
using System;
using System.Collections.Generic;
using System.IO;

namespace PersonaEditorLib.Sprite
{
    public sealed class TGA : IGameData, IImage
    {
        private const int HeaderSize = 18;
        private const int Rgba8Format = 0;

        private byte[] data;
        private readonly int width;
        private readonly int height;
        private readonly int imageDataOffset;
        private int rleDataEnd;
        private int textureDataOffset;
        private int textureDataLength;
        private Bitmap bitmap;

        public TGA(byte[] data)
        {
            this.data = Copy(data ?? throw new ArgumentNullException(nameof(data)));
            if (data.Length < HeaderSize)
                throw new InvalidDataException("TGA: file is too small.");
            if (data[1] != 0 || data[2] != 10 || data[16] != 32)
                throw new NotSupportedException("TGA: only NintendoWare RLE RGBA8 textures are supported.");

            width = ReadUInt16(data, 12);
            height = ReadUInt16(data, 14);
            if (width == 0 || height == 0)
                throw new InvalidDataException("TGA: invalid dimensions.");

            imageDataOffset = checked(HeaderSize + data[0]);
            if (imageDataOffset > data.Length)
                throw new InvalidDataException("TGA: image ID exceeds the file size.");

            DecodeTga(data, width, height, imageDataOffset, data[17], out rleDataEnd);
            FindTextureData(data, rleDataEnd, out textureDataOffset, out textureDataLength);
            if (textureDataLength != checked(NextPowerOfTwo(width) * NextPowerOfTwo(height) * 4))
                throw new InvalidDataException("TGA: unexpected nw4c_txd texture size.");

            // Validate the game texture as well as the standard TGA preview.
            CTPK.ReadCtrBitmap(CopyRange(data, textureDataOffset, textureDataLength), width, height, Rgba8Format);
        }

        public FormatEnum Type => FormatEnum.TGA;

        public List<GameFile> SubFiles { get; } = new List<GameFile>();

        public int GetSize() => data.Length;

        public byte[] GetData() => Copy(data);

        public Bitmap GetBitmap()
        {
            if (bitmap == null)
                bitmap = DecodeTga(data, width, height, imageDataOffset, data[17], out _);
            return bitmap;
        }

        public void SetBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                return;
            if (bitmap.Width != width || bitmap.Height != height)
                throw new ArgumentException($"TGA: image must be {width}x{height}.", nameof(bitmap));

            Bitmap source = bitmap.PixelFormat == PixelFormats.Bgra32 ? bitmap : bitmap.ConvertTo(PixelFormats.Bgra32, null);
            byte[] tgaPixels = EncodeTga(source, data[17]);
            byte[] texturePixels = CTPK.WriteCtrBitmap(source, Rgba8Format);
            if (texturePixels.Length != textureDataLength)
                throw new InvalidDataException("TGA: encoded nw4c_txd size does not match the original texture.");

            byte[] updated = new byte[checked(imageDataOffset + tgaPixels.Length + (textureDataOffset - rleDataEnd) + texturePixels.Length + (data.Length - textureDataOffset - textureDataLength))];
            int position = 0;
            position = CopyTo(data, 0, updated, position, imageDataOffset);
            position = CopyTo(tgaPixels, 0, updated, position, tgaPixels.Length);
            position = CopyTo(data, rleDataEnd, updated, position, textureDataOffset - rleDataEnd);
            position = CopyTo(texturePixels, 0, updated, position, texturePixels.Length);
            CopyTo(data, textureDataOffset + textureDataLength, updated, position, data.Length - textureDataOffset - textureDataLength);

            data = updated;
            DecodeTga(data, width, height, imageDataOffset, data[17], out rleDataEnd);
            FindTextureData(data, rleDataEnd, out textureDataOffset, out textureDataLength);
            this.bitmap = source;
        }

        private static Bitmap DecodeTga(byte[] source, int width, int height, int offset, byte descriptor, out int end)
        {
            int pixelCount = checked(width * height);
            byte[] storedPixels = new byte[checked(pixelCount * 4)];
            int output = 0;
            int position = offset;

            while (output < storedPixels.Length)
            {
                if (position >= source.Length)
                    throw new EndOfStreamException("TGA: unexpected end of RLE data.");

                int count = (source[position++] & 0x7F) + 1;
                int byteCount = checked(count * 4);
                if (byteCount > storedPixels.Length - output)
                    throw new InvalidDataException("TGA: RLE packet exceeds the image size.");

                if ((source[position - 1] & 0x80) != 0)
                {
                    if (position > source.Length - 4)
                        throw new EndOfStreamException("TGA: unexpected end of RLE run.");

                    for (int i = 0; i < count; i++)
                        Buffer.BlockCopy(source, position, storedPixels, output + i * 4, 4);
                    position += 4;
                }
                else
                {
                    if (position > source.Length - byteCount)
                        throw new EndOfStreamException("TGA: unexpected end of raw RLE packet.");

                    Buffer.BlockCopy(source, position, storedPixels, output, byteCount);
                    position += byteCount;
                }

                output += byteCount;
            }

            end = position;
            byte[] pixels = new byte[storedPixels.Length];
            bool topOrigin = (descriptor & 0x20) != 0;
            bool rightOrigin = (descriptor & 0x10) != 0;
            for (int sourceIndex = 0; sourceIndex < pixelCount; sourceIndex++)
            {
                int x = sourceIndex % width;
                int y = sourceIndex / width;
                int targetX = rightOrigin ? width - 1 - x : x;
                int targetY = topOrigin ? y : height - 1 - y;
                Buffer.BlockCopy(storedPixels, sourceIndex * 4, pixels, (targetY * width + targetX) * 4, 4);
            }

            return new Bitmap(width, height, PixelFormats.Bgra32, pixels, null);
        }

        private byte[] EncodeTga(Bitmap source, byte descriptor)
        {
            byte[] pixels = source.CopyData();
            byte[] storedPixels = new byte[pixels.Length];
            bool topOrigin = (descriptor & 0x20) != 0;
            bool rightOrigin = (descriptor & 0x10) != 0;
            for (int targetIndex = 0; targetIndex < width * height; targetIndex++)
            {
                int x = targetIndex % width;
                int y = targetIndex / width;
                int sourceX = rightOrigin ? width - 1 - x : x;
                int sourceY = topOrigin ? y : height - 1 - y;
                Buffer.BlockCopy(pixels, (sourceY * width + sourceX) * 4, storedPixels, targetIndex * 4, 4);
            }

            using (var stream = new MemoryStream())
            {
                for (int row = 0; row < height; row++)
                {
                    int pixel = row * width;
                    int rowEnd = pixel + width;
                    while (pixel < rowEnd)
                    {
                        int runLength = GetRunLength(storedPixels, pixel, rowEnd);
                        if (runLength > 1)
                        {
                            stream.WriteByte((byte)(0x80 | runLength - 1));
                            stream.Write(storedPixels, pixel * 4, 4);
                            pixel += runLength;
                            continue;
                        }

                        int first = pixel++;
                        while (pixel < rowEnd && pixel - first < 128 && GetRunLength(storedPixels, pixel, rowEnd) == 1)
                            pixel++;

                        stream.WriteByte((byte)(pixel - first - 1));
                        stream.Write(storedPixels, first * 4, (pixel - first) * 4);
                    }
                }

                return stream.ToArray();
            }
        }

        private static int GetRunLength(byte[] pixels, int first, int end)
        {
            int length = 1;
            while (first + length < end && length < 128 && PixelsEqual(pixels, first, first + length))
                length++;
            return length;
        }

        private static bool PixelsEqual(byte[] pixels, int first, int second)
        {
            int firstOffset = first * 4;
            int secondOffset = second * 4;
            return pixels[firstOffset] == pixels[secondOffset]
                && pixels[firstOffset + 1] == pixels[secondOffset + 1]
                && pixels[firstOffset + 2] == pixels[secondOffset + 2]
                && pixels[firstOffset + 3] == pixels[secondOffset + 3];
        }

        private static void FindTextureData(byte[] source, int offset, out int textureOffset, out int textureLength)
        {
            if (!HasChunk(source, offset, "nw4c_tfm", out int formatChunkSize)
                || formatChunkSize != 17
                || !HasAscii(source, offset + 12, "Rgba8"))
                throw new InvalidDataException("TGA: missing nw4c Rgba8 format metadata.");

            int textureChunk = checked(offset + formatChunkSize);
            if (!HasChunk(source, textureChunk, "nw4c_txd", out int textureChunkSize) || textureChunkSize < 12)
                throw new InvalidDataException("TGA: missing nw4c texture data.");

            textureOffset = textureChunk + 12;
            textureLength = textureChunkSize - 12;
            if (textureLength > source.Length - textureOffset)
                throw new InvalidDataException("TGA: nw4c texture data exceeds the file size.");
        }

        private static bool HasChunk(byte[] source, int offset, string name, out int size)
        {
            size = 0;
            if (!HasAscii(source, offset, name) || offset > source.Length - 12)
                return false;

            size = ReadInt32(source, offset + 8);
            return size >= 12 && size <= source.Length - offset;
        }

        private static bool HasAscii(byte[] source, int offset, string value)
        {
            if (offset < 0 || value.Length > source.Length - offset)
                return false;

            for (int i = 0; i < value.Length; i++)
                if (source[offset + i] != value[i])
                    return false;
            return true;
        }

        private static ushort ReadUInt16(byte[] source, int offset)
            => (ushort)(source[offset] | source[offset + 1] << 8);

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
                result <<= 1;
            return result;
        }

        private static int ReadInt32(byte[] source, int offset)
            => source[offset] | source[offset + 1] << 8 | source[offset + 2] << 16 | source[offset + 3] << 24;

        private static byte[] Copy(byte[] source)
            => CopyRange(source, 0, source.Length);

        private static byte[] CopyRange(byte[] source, int offset, int length)
        {
            byte[] copy = new byte[length];
            Buffer.BlockCopy(source, offset, copy, 0, length);
            return copy;
        }

        private static int CopyTo(byte[] source, int sourceOffset, byte[] destination, int destinationOffset, int length)
        {
            Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, length);
            return destinationOffset + length;
        }
    }
}
