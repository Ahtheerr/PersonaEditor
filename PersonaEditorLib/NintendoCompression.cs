using System;
using System.IO;

namespace PersonaEditorLib
{
    internal static class NintendoCompression
    {
        public static byte[] Decompress(byte[] data)
        {
            if (data == null || data.Length < 4)
                throw new InvalidDataException("CMP: missing compression header.");

            byte method = data[0];
            int size = data[1] | data[2] << 8 | data[3] << 16;
            if (size < 0)
                throw new InvalidDataException("CMP: invalid decompressed size.");

            return method switch
            {
                0x10 => DecompressLz10(data, size),
                0x11 => DecompressLz11(data, size),
                0x30 => DecompressRle(data, size),
                _ => throw new InvalidDataException("CMP: unsupported compression header.")
            };
        }

        public static bool TryDecompress(byte[] data, out byte[] decompressed)
        {
            try
            {
                decompressed = Decompress(data);
                return true;
            }
            catch
            {
                decompressed = null;
                return false;
            }
        }

        private static byte[] DecompressLz10(byte[] data, int size)
        {
            byte[] output = new byte[size];
            int inputOffset = 4;
            int outputOffset = 0;

            while (outputOffset < output.Length)
            {
                if (inputOffset >= data.Length)
                    throw new InvalidDataException("CMP: truncated LZ10 data.");

                byte flags = data[inputOffset++];
                for (int i = 0; i < 8 && outputOffset < output.Length; i++, flags <<= 1)
                {
                    if ((flags & 0x80) == 0)
                    {
                        if (inputOffset >= data.Length)
                            throw new InvalidDataException("CMP: truncated LZ10 literal.");

                        output[outputOffset++] = data[inputOffset++];
                        continue;
                    }

                    if (inputOffset + 1 >= data.Length)
                        throw new InvalidDataException("CMP: truncated LZ10 back-reference.");

                    int value = data[inputOffset] << 8 | data[inputOffset + 1];
                    inputOffset += 2;
                    int length = (value >> 12) + 3;
                    int distance = (value & 0xFFF) + 1;
                    CopyBackReference(output, ref outputOffset, distance, length);
                }
            }

            return output;
        }

        private static byte[] DecompressLz11(byte[] data, int size)
        {
            byte[] output = new byte[size];
            int inputOffset = 4;
            int outputOffset = 0;

            while (outputOffset < output.Length)
            {
                if (inputOffset >= data.Length)
                    throw new InvalidDataException("CMP: truncated LZ11 data.");

                byte flags = data[inputOffset++];
                for (int i = 0; i < 8 && outputOffset < output.Length; i++, flags <<= 1)
                {
                    if ((flags & 0x80) == 0)
                    {
                        if (inputOffset >= data.Length)
                            throw new InvalidDataException("CMP: truncated LZ11 literal.");

                        output[outputOffset++] = data[inputOffset++];
                        continue;
                    }

                    if (inputOffset + 1 >= data.Length)
                        throw new InvalidDataException("CMP: truncated LZ11 back-reference.");

                    int a = data[inputOffset++];
                    int b = data[inputOffset++];
                    int length;
                    int distance;
                    int kind = a >> 4;
                    if (kind == 0)
                    {
                        if (inputOffset >= data.Length)
                            throw new InvalidDataException("CMP: truncated LZ11 long back-reference.");

                        int c = data[inputOffset++];
                        length = ((a & 0xF) << 4 | b >> 4) + 0x11;
                        distance = ((b & 0xF) << 8 | c) + 1;
                    }
                    else if (kind == 1)
                    {
                        if (inputOffset + 1 >= data.Length)
                            throw new InvalidDataException("CMP: truncated LZ11 extended back-reference.");

                        int c = data[inputOffset++];
                        int d = data[inputOffset++];
                        length = ((a & 0xF) << 12 | b << 4 | c >> 4) + 0x111;
                        distance = ((c & 0xF) << 8 | d) + 1;
                    }
                    else
                    {
                        length = kind + 1;
                        distance = ((a & 0xF) << 8 | b) + 1;
                    }

                    CopyBackReference(output, ref outputOffset, distance, length);
                }
            }

            return output;
        }

        private static byte[] DecompressRle(byte[] data, int size)
        {
            byte[] output = new byte[size];
            int inputOffset = 4;
            int outputOffset = 0;

            while (outputOffset < output.Length)
            {
                if (inputOffset >= data.Length)
                    throw new InvalidDataException("CMP: truncated RLE data.");

                byte control = data[inputOffset++];
                bool compressed = (control & 0x80) != 0;
                int length = (control & 0x7F) + (compressed ? 3 : 1);
                if (length > output.Length - outputOffset)
                    throw new InvalidDataException("CMP: RLE block exceeds the decompressed size.");

                if (compressed)
                {
                    if (inputOffset >= data.Length)
                        throw new InvalidDataException("CMP: truncated RLE run.");

                    byte value = data[inputOffset++];
                    for (int i = 0; i < length; i++)
                        output[outputOffset++] = value;
                }
                else
                {
                    if (length > data.Length - inputOffset)
                        throw new InvalidDataException("CMP: truncated RLE literal block.");

                    Buffer.BlockCopy(data, inputOffset, output, outputOffset, length);
                    inputOffset += length;
                    outputOffset += length;
                }
            }

            return output;
        }

        private static void CopyBackReference(byte[] output, ref int outputOffset, int distance, int length)
        {
            int source = outputOffset - distance;
            if (source < 0)
                throw new InvalidDataException("CMP: invalid back-reference.");

            for (int i = 0; i < length && outputOffset < output.Length; i++)
                output[outputOffset++] = output[source + i];
        }
    }
}
