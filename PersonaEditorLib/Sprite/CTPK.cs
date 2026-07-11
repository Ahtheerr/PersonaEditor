using AuxiliaryLibraries.Media;
using AuxiliaryLibraries.Tools;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PersonaEditorLib.Sprite
{
    public sealed class CTPK : IGameData, IImage
    {
        private CtpkHeader header;
        private readonly List<CtpkEntry> entries = new List<CtpkEntry>();
        private readonly List<Bitmap> bitmaps = new List<Bitmap>();

        private static readonly Dictionary<int, ICtpkImageFormat> CtrFormats = new Dictionary<int, ICtpkImageFormat>
        {
            [0] = new RgbaFormat(8, 8, 8, 8),
            [1] = new RgbaFormat(8, 8, 8),
            [2] = new RgbaFormat(5, 5, 5, 1),
            [3] = new RgbaFormat(5, 6, 5),
            [4] = new RgbaFormat(4, 4, 4, 4),
            [5] = new LaFormat(8, 8),
            [6] = new HlFormat(8, 8),
            [7] = new LaFormat(8, 0),
            [8] = new LaFormat(0, 8),
            [9] = new LaFormat(4, 4),
            [10] = new LaFormat(4, 0),
            [11] = new LaFormat(0, 4),
            [12] = new Etc1Format(),
            [13] = new Etc1Format(true),
        };

        public CTPK(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                header = ReadStruct<CtpkHeader>(br);
                if (header.TexCount <= 0)
                    throw new Exception("CTPK: empty texture list");

                for (int i = 0; i < header.TexCount; i++)
                    entries.Add(new CtpkEntry());

                br.BaseStream.Position = 0x20;
                foreach (var entry in entries)
                    entry.TexEntry = ReadStruct<CtpkTexEntry>(br);

                foreach (var entry in entries)
                    for (int i = 0; i < entry.TexEntry.MipLevel; i++)
                        entry.DataSizes.Add(br.ReadInt32());

                foreach (var entry in entries)
                    entry.Name = ReadCString(br);

                br.BaseStream.Position = header.Crc32SectionOffset;
                var hashList = new List<CtpkHashEntry>();
                for (int i = 0; i < header.TexCount; i++)
                    hashList.Add(ReadStruct<CtpkHashEntry>(br));
                hashList = hashList.OrderBy(x => x.Id).ToList();
                for (int i = 0; i < entries.Count; i++)
                    entries[i].Hash = hashList[i];

                br.BaseStream.Position = header.TexInfoOffset;
                foreach (var entry in entries)
                    entry.MipmapEntry = ReadStruct<CtpkMipmapEntry>(br);

                br.BaseStream.Position = header.TexSectionOffset;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    br.BaseStream.Position = entry.TexEntry.TexOffset + header.TexSectionOffset;
                    bitmaps.Add(ReadBitmap(br.ReadBytes(GetDataSize(entry, 0)), entry.TexEntry.Width, entry.TexEntry.Height, entry.TexEntry.ImageFormat));

                    if (entry.TexEntry.MipLevel > 1)
                    {
                        var mipWidth = entry.TexEntry.Width;
                        var mipHeight = entry.TexEntry.Height;
                        for (int mip = 1; mip < entry.TexEntry.MipLevel; mip++)
                        {
                            mipWidth >>= 1;
                            mipHeight >>= 1;
                            bitmaps.Add(ReadBitmap(br.ReadBytes(GetDataSize(entry, mip)), mipWidth, mipHeight, entry.MipmapEntry.MipmapFormat));
                        }
                    }
                }
            }
        }

        public FormatEnum Type => FormatEnum.CTPK;

        public List<GameFile> SubFiles { get; } = new List<GameFile>();

        public int GetSize()
        {
            ValidateBitmapSizes();

            int size = header.TexSectionOffset;
            int bitmapIndex = 0;
            foreach (var entry in entries)
            {
                size += GetBitmapDataSize(bitmaps[bitmapIndex++], entry.TexEntry.ImageFormat);
                for (int mip = 1; mip < entry.TexEntry.MipLevel; mip++)
                    size += GetBitmapDataSize(bitmaps[bitmapIndex++], entry.MipmapEntry.MipmapFormat);
            }
            return size;
        }

        public byte[] GetData()
        {
            ValidateBitmapSizes();

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                WriteStruct(bw, header);
                bw.BaseStream.Position = 0x20;

                foreach (var entry in entries)
                    WriteStruct(bw, entry.TexEntry);

                foreach (var entry in entries)
                    foreach (var dataSize in entry.DataSizes)
                        bw.Write(dataSize);

                foreach (var entry in entries)
                    bw.Write(System.Text.Encoding.ASCII.GetBytes(entry.Name + "\0"));

                bw.BaseStream.Position = (bw.BaseStream.Position + 0x3) & ~0x3;
                foreach (var hash in entries.Select(x => x.Hash).OrderBy(x => x.Crc32))
                    WriteStruct(bw, hash);

                foreach (var entry in entries)
                    WriteStruct(bw, entry.MipmapEntry);

                bw.BaseStream.Position = header.TexSectionOffset;
                int bitmapIndex = 0;
                foreach (var entry in entries)
                {
                    bw.Write(WriteBitmap(bitmaps[bitmapIndex++], entry.TexEntry.ImageFormat));
                    if (entry.TexEntry.MipLevel > 1)
                    {
                        for (int mip = 1; mip < entry.TexEntry.MipLevel; mip++)
                            bw.Write(WriteBitmap(bitmaps[bitmapIndex++], entry.MipmapEntry.MipmapFormat));
                    }
                }

                return ms.ToArray();
            }
        }

        public Bitmap GetBitmap()
        {
            return GetBitmap(0);
        }

        public void SetBitmap(Bitmap bitmap)
        {
            SetBitmap(0, bitmap);
        }

        public int TextureCount => entries.Count;

        public string GetTextureName(int textureIndex)
        {
            if (textureIndex < 0 || textureIndex >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(textureIndex));
            return entries[textureIndex].Name;
        }

        public Bitmap GetBitmap(int textureIndex)
        {
            if (entries.Count == 0)
                return null;

            int bitmapIndex = GetBaseBitmapIndex(textureIndex);
            return bitmaps[bitmapIndex];
        }

        public void SetBitmap(int textureIndex, Bitmap bitmap)
        {
            if (bitmap == null || entries.Count == 0)
                return;

            int bitmapIndex = GetBaseBitmapIndex(textureIndex);
            var expected = entries[textureIndex].TexEntry;
            if (bitmap.Width != expected.Width || bitmap.Height != expected.Height)
                throw new Exception($"CTPK: image must be {expected.Width}x{expected.Height}");

            bitmaps[bitmapIndex] = bitmap.PixelFormat == PixelFormats.Bgra32 ? bitmap : bitmap.ConvertTo(PixelFormats.Bgra32, null);
        }

        private int GetBaseBitmapIndex(int textureIndex)
        {
            if (textureIndex < 0 || textureIndex >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(textureIndex));

            int bitmapIndex = 0;
            for (int i = 0; i < textureIndex; i++)
                bitmapIndex += entries[i].TexEntry.MipLevel;
            return bitmapIndex;
        }

        private static int GetDataSize(CtpkEntry entry, int mipIndex)
        {
            if (mipIndex < entry.DataSizes.Count && entry.DataSizes[mipIndex] != 0)
                return entry.DataSizes[mipIndex];
            return entry.TexEntry.TexDataSize;
        }

        private static Bitmap ReadBitmap(byte[] data, int width, int height, int formatIndex)
        {
            if (!CtrFormats.ContainsKey(formatIndex))
                throw new Exception($"CTPK: unsupported texture format {formatIndex}");

            var settings = new CtpkImageSettings
            {
                Width = width,
                Height = height,
                Format = CtrFormats[formatIndex],
                Swizzle = new CtrSwizzle(width, height)
            };
            return CtpkImageCodec.Load(data, settings);
        }

        internal static Bitmap ReadCtrBitmap(byte[] data, int width, int height, int formatIndex)
            => ReadBitmap(data, width, height, formatIndex);

        internal static byte[] WriteCtrBitmap(Bitmap bitmap, int formatIndex)
            => WriteBitmap(bitmap, formatIndex);

        private static byte[] WriteBitmap(Bitmap bitmap, int formatIndex)
        {
            if (!CtrFormats.ContainsKey(formatIndex))
                throw new Exception($"CTPK: unsupported texture format {formatIndex}");

            var settings = new CtpkImageSettings
            {
                Width = bitmap.Width,
                Height = bitmap.Height,
                Format = CtrFormats[formatIndex],
                Swizzle = new CtrSwizzle(bitmap.Width, bitmap.Height)
            };
            return CtpkImageCodec.Save(bitmap, settings);
        }

        private static int GetBitmapDataSize(Bitmap bitmap, int formatIndex)
        {
            if (!CtrFormats.TryGetValue(formatIndex, out var format))
                throw new Exception($"CTPK: unsupported texture format {formatIndex}");

            var swizzle = new CtrSwizzle(bitmap.Width, bitmap.Height);
            return (swizzle.Width * swizzle.Height * format.BitDepth + 7) / 8;
        }

        private void ValidateBitmapSizes()
        {
            int index = 0;
            foreach (var entry in entries)
            {
                int width = entry.TexEntry.Width;
                int height = entry.TexEntry.Height;
                if (bitmaps[index].Width != width || bitmaps[index].Height != height)
                    throw new Exception($"CTPK: texture {index} must be {width}x{height}");
                index++;
                for (int mip = 1; mip < entry.TexEntry.MipLevel; mip++)
                {
                    width >>= 1;
                    height >>= 1;
                    if (bitmaps[index].Width != width || bitmaps[index].Height != height)
                        throw new Exception($"CTPK: mipmap {index} must be {width}x{height}");
                    index++;
                }
            }
        }

        private static string ReadCString(BinaryReader br)
        {
            var bytes = new List<byte>();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                var b = br.ReadByte();
                if (b == 0)
                    break;
                bytes.Add(b);
            }
            return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static T ReadStruct<T>(BinaryReader br) where T : struct
            => IOTools.FromBytes<T>(br.ReadBytes(Marshal.SizeOf(typeof(T))));

        private static void WriteStruct<T>(BinaryWriter bw, T value) where T : struct
            => bw.Write(IOTools.GetBytes(value));

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CtpkHeader
        {
            public int Magic;
            public short Version;
            public short TexCount;
            public int TexSectionOffset;
            public int TexSectionSize;
            public int Crc32SectionOffset;
            public int TexInfoOffset;
        }

        private class CtpkEntry
        {
            public CtpkTexEntry TexEntry;
            public List<int> DataSizes = new List<int>();
            public string Name;
            public CtpkHashEntry Hash;
            public CtpkMipmapEntry MipmapEntry;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CtpkTexEntry
        {
            public int NameOffset;
            public int TexDataSize;
            public int TexOffset;
            public int ImageFormat;
            public short Width;
            public short Height;
            public byte MipLevel;
            public byte Type;
            public short Zero0;
            public int BitmapSizeOffset;
            public uint TimeStamp;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CtpkHashEntry
        {
            public uint Crc32;
            public int Id;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CtpkMipmapEntry
        {
            public byte MipmapFormat;
            public byte MipLevel;
            public byte Compression;
            public byte CompressionMethod;
        }

        private class CtpkImageSettings
        {
            public int Width;
            public int Height;
            public ICtpkImageFormat Format;
            public ICtpkImageSwizzle Swizzle;
            public Func<Color, Color> PixelShader;
        }

        private interface ICtpkImageFormat
        {
            int BitDepth { get; }
            string FormatName { get; }
            IEnumerable<Color> Load(byte[] input);
            byte[] Save(IEnumerable<Color> colors);
        }

        private interface ICtpkImageSwizzle
        {
            int Width { get; }
            int Height { get; }
            Point Get(Point point);
        }

        private static class CtpkImageCodec
        {
            private static IEnumerable<Point> GetPointSequence(CtpkImageSettings settings)
            {
                int strideWidth = settings.Swizzle != null ? settings.Swizzle.Width : settings.Width;
                int strideHeight = settings.Swizzle != null ? settings.Swizzle.Height : settings.Height;

                for (int i = 0; i < strideWidth * strideHeight; i++)
                {
                    var point = new Point(i % strideWidth, i / strideWidth);
                    if (settings.Swizzle != null)
                        point = settings.Swizzle.Get(point);
                    yield return point;
                }
            }

            public static Bitmap Load(byte[] data, CtpkImageSettings settings)
            {
                int width = settings.Width;
                int height = settings.Height;
                var pixels = new Color[width * height];

                foreach (var pair in GetPointSequence(settings).Zip(settings.Format.Load(data), Tuple.Create))
                {
                    int x = pair.Item1.X;
                    int y = pair.Item1.Y;
                    if (0 <= x && x < width && 0 <= y && y < height)
                    {
                        var color = pair.Item2;
                        if (settings.PixelShader != null)
                            color = settings.PixelShader(color);
                        pixels[y * width + x] = color;
                    }
                }

                return new Bitmap(width, height, pixels).ConvertTo(PixelFormats.Bgra32, null);
            }

            public static byte[] Save(Bitmap bitmap, CtpkImageSettings settings)
            {
                var source = bitmap.PixelFormat == PixelFormats.Bgra32 ? bitmap : bitmap.ConvertTo(PixelFormats.Bgra32, null);
                var pixels = source.CopyPixels();
                var colors = new List<Color>(settings.Swizzle.Width * settings.Swizzle.Height);

                foreach (var point in GetPointSequence(settings))
                {
                    int x = Clamp(point.X, 0, source.Width);
                    int y = Clamp(point.Y, 0, source.Height);
                    var color = pixels[y * source.Width + x];
                    if (settings.PixelShader != null)
                        color = settings.PixelShader(color);
                    colors.Add(color);
                }

                return settings.Format.Save(colors);
            }

            private static int Clamp(int value, int min, int max)
                => Math.Min(Math.Max(value, min), max - 1);
        }

        private class CtrSwizzle : ICtpkImageSwizzle
        {
            private readonly byte orientation;
            private readonly MasterSwizzle zOrder;

            public int Width { get; }
            public int Height { get; }

            public CtrSwizzle(int width, int height, byte orientation = 0, bool toPowerOf2 = true)
            {
                Width = toPowerOf2 ? 2 << (int)Math.Log(width - 1, 2) : width;
                Height = toPowerOf2 ? 2 << (int)Math.Log(height - 1, 2) : height;
                this.orientation = orientation;
                zOrder = new MasterSwizzle(orientation == 0 || orientation == 2 ? Width : Height, new Point(0, 0),
                    new[] { (1, 0), (0, 1), (2, 0), (0, 2), (4, 0), (0, 4) });
            }

            public Point Get(Point point)
            {
                int pointCount = point.Y * Width + point.X;
                var newPoint = zOrder.Get(pointCount);
                switch (orientation)
                {
                    case 8: return new Point(newPoint.Y, newPoint.X);
                    case 4: return new Point(newPoint.Y, Height - 1 - newPoint.X);
                    case 2: return new Point(newPoint.X, Height - 1 - newPoint.Y);
                    default: return newPoint;
                }
            }
        }

        private class MasterSwizzle
        {
            private readonly IEnumerable<(int, int)> bitFieldCoords;
            private readonly IEnumerable<(int, int)> initPointTransformOnY;
            private readonly int widthInTiles;
            private readonly Point init;

            public int MacroTileWidth { get; }
            public int MacroTileHeight { get; }

            public MasterSwizzle(int imageStride, Point init, IEnumerable<(int, int)> bitFieldCoords, IEnumerable<(int, int)> initPointTransformOnY = null)
            {
                this.bitFieldCoords = bitFieldCoords;
                this.initPointTransformOnY = initPointTransformOnY ?? Enumerable.Empty<(int, int)>();
                this.init = init;

                MacroTileWidth = bitFieldCoords.Select(p => p.Item1).Aggregate((x, y) => x | y) + 1;
                MacroTileHeight = bitFieldCoords.Select(p => p.Item2).Aggregate((x, y) => x | y) + 1;
                widthInTiles = (imageStride + MacroTileWidth - 1) / MacroTileWidth;
            }

            public Point Get(int pointCount)
            {
                int macroTileCount = pointCount / MacroTileWidth / MacroTileHeight;
                int macroX = macroTileCount % widthInTiles;
                int macroY = macroTileCount / widthInTiles;

                return new[] { (macroX * MacroTileWidth, macroY * MacroTileHeight) }
                    .Concat(bitFieldCoords.Where((v, j) => (pointCount >> j) % 2 == 1))
                    .Concat(initPointTransformOnY.Where((v, j) => (macroY >> j) % 2 == 1))
                    .Aggregate(init, (a, b) => new Point(a.X ^ b.Item1, a.Y ^ b.Item2));
            }
        }

        private static class CtpkFormatSupport
        {
            public static int ChangeBitDepth(int value, int bitDepthFrom, int bitDepthTo)
            {
                if (bitDepthFrom == 0 || bitDepthTo == 0)
                    return 0;
                if (bitDepthFrom == bitDepthTo)
                    return value;

                if (bitDepthFrom < bitDepthTo)
                {
                    int fromMaxRange = (1 << bitDepthFrom) - 1;
                    int toMaxRange = (1 << bitDepthTo) - 1;
                    int div = 1;
                    while (toMaxRange % fromMaxRange != 0)
                    {
                        div <<= 1;
                        toMaxRange = ((toMaxRange + 1) << 1) - 1;
                    }
                    return value * (toMaxRange / fromMaxRange) / div;
                }

                int fromMax = 1 << bitDepthFrom;
                int toMax = 1 << bitDepthTo;
                int limit = fromMax / toMax;
                return value / limit;
            }
        }

        private class RgbaFormat : ICtpkImageFormat
        {
            public int BitDepth { get; }
            public string FormatName { get; }
            private readonly int rDepth;
            private readonly int gDepth;
            private readonly int bDepth;
            private readonly int aDepth;

            public RgbaFormat(int r, int g, int b, int a = 0)
            {
                BitDepth = r + g + b + a;
                rDepth = r;
                gDepth = g;
                bDepth = b;
                aDepth = a;
                FormatName = "RGBA";
            }

            public IEnumerable<Color> Load(byte[] input)
            {
                using (var br = new BinaryReader(new MemoryStream(input)))
                {
                    int aShift = 0;
                    int bShift = aDepth;
                    int gShift = bShift + bDepth;
                    int rShift = gShift + gDepth;

                    int aMask = (1 << aDepth) - 1;
                    int bMask = (1 << bDepth) - 1;
                    int gMask = (1 << gDepth) - 1;
                    int rMask = (1 << rDepth) - 1;

                    while (br.BaseStream.Position < br.BaseStream.Length)
                    {
                        long value;
                        if (BitDepth <= 8)
                            value = br.ReadByte();
                        else if (BitDepth <= 16)
                            value = br.ReadUInt16();
                        else if (BitDepth <= 24)
                        {
                            var t = br.ReadBytes(3);
                            value = t[2] << 16 | t[1] << 8 | t[0];
                        }
                        else
                            value = br.ReadUInt32();

                        yield return Color.FromArgb(
                            aDepth == 0 ? 255 : CtpkFormatSupport.ChangeBitDepth((int)(value >> aShift & aMask), aDepth, 8),
                            CtpkFormatSupport.ChangeBitDepth((int)(value >> rShift & rMask), rDepth, 8),
                            CtpkFormatSupport.ChangeBitDepth((int)(value >> gShift & gMask), gDepth, 8),
                            CtpkFormatSupport.ChangeBitDepth((int)(value >> bShift & bMask), bDepth, 8));
                    }
                }
            }

            public byte[] Save(IEnumerable<Color> colors)
            {
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    foreach (var color in colors)
                    {
                        int a = aDepth == 0 ? 0 : CtpkFormatSupport.ChangeBitDepth(color.A, 8, aDepth);
                        int r = CtpkFormatSupport.ChangeBitDepth(color.R, 8, rDepth);
                        int g = CtpkFormatSupport.ChangeBitDepth(color.G, 8, gDepth);
                        int b = CtpkFormatSupport.ChangeBitDepth(color.B, 8, bDepth);

                        int aShift = 0;
                        int bShift = aDepth;
                        int gShift = bShift + bDepth;
                        int rShift = gShift + gDepth;

                        long value = 0;
                        value |= (uint)(a << aShift);
                        value |= (uint)(b << bShift);
                        value |= (uint)(g << gShift);
                        value |= (uint)(r << rShift);

                        if (BitDepth <= 8)
                            bw.Write((byte)value);
                        else if (BitDepth <= 16)
                            bw.Write((ushort)value);
                        else if (BitDepth <= 24)
                            bw.Write(new[] { (byte)(value & 0xff), (byte)(value >> 8 & 0xff), (byte)(value >> 16 & 0xff) });
                        else
                            bw.Write((uint)value);
                    }
                    return ms.ToArray();
                }
            }
        }

        private class LaFormat : ICtpkImageFormat
        {
            public int BitDepth { get; }
            public string FormatName { get; }
            private readonly int lDepth;
            private readonly int aDepth;

            public LaFormat(int l, int a)
            {
                BitDepth = l + a;
                lDepth = l;
                aDepth = a;
                FormatName = "LA";
            }

            public IEnumerable<Color> Load(byte[] input)
            {
                using (var br = new BinaryReader(new MemoryStream(input)))
                {
                    int lShift = aDepth;
                    int aMask = (1 << aDepth) - 1;
                    int lMask = (1 << lDepth) - 1;
                    int nibble = -1;

                    while (true)
                    {
                        long value;
                        switch (BitDepth)
                        {
                            case 4:
                                value = ReadNibble(br, ref nibble);
                                break;
                            case 8:
                                value = br.ReadByte();
                                break;
                            case 16:
                                value = br.ReadUInt16();
                                break;
                            default:
                                throw new Exception("CTPK: unsupported LA bit depth");
                        }

                        int l = lDepth == 0 ? 255 : CtpkFormatSupport.ChangeBitDepth((int)(value >> lShift & lMask), lDepth, 8);
                        int a = aDepth == 0 ? 255 : CtpkFormatSupport.ChangeBitDepth((int)(value & aMask), aDepth, 8);
                        yield return Color.FromArgb(a, l, l, l);
                    }
                }
            }

            public byte[] Save(IEnumerable<Color> colors)
            {
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    int nibble = -1;
                    foreach (var color in colors)
                    {
                        int a = aDepth == 0 ? 0 : CtpkFormatSupport.ChangeBitDepth(color.A, 8, aDepth);
                        int l = lDepth == 0 ? 0 : CtpkFormatSupport.ChangeBitDepth(color.G, 8, lDepth);
                        long value = a | ((uint)l << aDepth);
                        switch (BitDepth)
                        {
                            case 4:
                                WriteNibble(bw, ref nibble, (int)value);
                                break;
                            case 8:
                                bw.Write((byte)value);
                                break;
                            case 16:
                                bw.Write((ushort)value);
                                break;
                            default:
                                throw new Exception("CTPK: unsupported LA bit depth");
                        }
                    }
                    return ms.ToArray();
                }
            }
        }

        private class HlFormat : ICtpkImageFormat
        {
            public int BitDepth { get; }
            public string FormatName { get; }
            private readonly int rDepth;
            private readonly int gDepth;

            public HlFormat(int r, int g)
            {
                BitDepth = r + g;
                rDepth = r;
                gDepth = g;
                FormatName = "HL";
            }

            public IEnumerable<Color> Load(byte[] input)
            {
                using (var br = new BinaryReader(new MemoryStream(input)))
                {
                    int rShift = gDepth;
                    int gMask = (1 << gDepth) - 1;
                    int rMask = (1 << rDepth) - 1;
                    int nibble = -1;

                    while (br.BaseStream.Position < br.BaseStream.Length)
                    {
                        long value;
                        switch (BitDepth)
                        {
                            case 4:
                                value = ReadNibble(br, ref nibble);
                                break;
                            case 8:
                                value = br.ReadByte();
                                break;
                            case 16:
                                value = br.ReadUInt16();
                                break;
                            default:
                                throw new Exception("CTPK: unsupported HL bit depth");
                        }

                        int r = rDepth == 0 ? 255 : CtpkFormatSupport.ChangeBitDepth((int)(value >> rShift & rMask), rDepth, 8);
                        int g = gDepth == 0 ? 255 : CtpkFormatSupport.ChangeBitDepth((int)(value & gMask), gDepth, 8);
                        yield return Color.FromArgb(255, r, g, 255);
                    }
                }
            }

            public byte[] Save(IEnumerable<Color> colors)
            {
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    int nibble = -1;
                    foreach (var color in colors)
                    {
                        int r = rDepth == 0 ? 0 : CtpkFormatSupport.ChangeBitDepth(color.R, 8, rDepth);
                        int g = gDepth == 0 ? 0 : CtpkFormatSupport.ChangeBitDepth(color.G, 8, gDepth);
                        long value = g | ((uint)r << gDepth);
                        switch (BitDepth)
                        {
                            case 4:
                                WriteNibble(bw, ref nibble, (int)value);
                                break;
                            case 8:
                                bw.Write((byte)value);
                                break;
                            case 16:
                                bw.Write((ushort)value);
                                break;
                            default:
                                throw new Exception("CTPK: unsupported HL bit depth");
                        }
                    }
                    return ms.ToArray();
                }
            }
        }

        private class Etc1Format : ICtpkImageFormat
        {
            public int BitDepth { get; }
            public string FormatName { get; }
            private readonly bool alpha;

            public Etc1Format(bool alpha = false)
            {
                this.alpha = alpha;
                BitDepth = alpha ? 8 : 4;
                FormatName = alpha ? "ETC1A4" : "ETC1";
            }

            public IEnumerable<Color> Load(byte[] input)
            {
                using (var br = new BinaryReader(new MemoryStream(input)))
                {
                    var decoder = new Etc1Support.Decoder(true);
                    while (true)
                    {
                        yield return decoder.Get(() =>
                        {
                            ulong etcAlpha = alpha ? br.ReadUInt64() : ulong.MaxValue;
                            ulong colorBlock = br.ReadUInt64();
                            var block = new Etc1Support.Block
                            {
                                LSB = (ushort)(colorBlock & 0xFFFF),
                                MSB = (ushort)((colorBlock >> 16) & 0xFFFF),
                                Flags = (byte)((colorBlock >> 32) & 0xFF),
                                B = (byte)((colorBlock >> 40) & 0xFF),
                                G = (byte)((colorBlock >> 48) & 0xFF),
                                R = (byte)((colorBlock >> 56) & 0xFF),
                            };
                            return new Etc1Support.PixelData { Alpha = etcAlpha, Block = block };
                        });
                    }
                }
            }

            public byte[] Save(IEnumerable<Color> colors)
            {
                var encoder = new Etc1Support.Encoder(true);
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    foreach (var color in colors)
                    {
                        encoder.Set(color, data =>
                        {
                            if (alpha)
                                bw.Write(data.Alpha);
                            ulong colorBlock = 0;
                            colorBlock |= data.Block.LSB;
                            colorBlock |= ((ulong)data.Block.MSB << 16);
                            colorBlock |= ((ulong)data.Block.Flags << 32);
                            colorBlock |= ((ulong)data.Block.B << 40);
                            colorBlock |= ((ulong)data.Block.G << 48);
                            colorBlock |= ((ulong)data.Block.R << 56);
                            bw.Write(colorBlock);
                        });
                    }
                    return ms.ToArray();
                }
            }
        }

        private static int ReadNibble(BinaryReader br, ref int state)
        {
            if (state == -1)
            {
                state = br.ReadByte();
                return state % 16;
            }
            int value = state / 16;
            state = -1;
            return value;
        }

        private static void WriteNibble(BinaryWriter bw, ref int state, int value)
        {
            value &= 15;
            if (state == -1)
                state = value;
            else
            {
                bw.Write((byte)(state + 16 * value));
                state = -1;
            }
        }

        // Adapted from Kuriimu Kontract.Image.Support.ETC1 with namespace/style changes only.
        private static class Etc1Support
        {
            private static readonly int[] Order3ds = { 0, 4, 1, 5, 8, 12, 9, 13, 2, 6, 3, 7, 10, 14, 11, 15 };
            private static readonly int[] OrderNormal = { 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15 };

            private static readonly int[][] Modifiers =
            {
                new[] { 2, 8, -2, -8 },
                new[] { 5, 17, -5, -17 },
                new[] { 9, 29, -9, -29 },
                new[] { 13, 42, -13, -42 },
                new[] { 18, 60, -18, -60 },
                new[] { 24, 80, -24, -80 },
                new[] { 33, 106, -33, -106 },
                new[] { 47, 183, -47, -183 }
            };

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct Block
            {
                public ushort LSB;
                public ushort MSB;
                public byte Flags;
                public byte B;
                public byte G;
                public byte R;

                public bool FlipBit
                {
                    get => (Flags & 1) == 1;
                    set => Flags = (byte)((Flags & ~1) | (value ? 1 : 0));
                }

                public bool DiffBit
                {
                    get => (Flags & 2) == 2;
                    set => Flags = (byte)((Flags & ~2) | (value ? 2 : 0));
                }

                public int ColorDepth => DiffBit ? 32 : 16;
                public int Table0
                {
                    get => (Flags >> 5) & 7;
                    set => Flags = (byte)((Flags & ~(7 << 5)) | (value << 5));
                }
                public int Table1
                {
                    get => (Flags >> 2) & 7;
                    set => Flags = (byte)((Flags & ~(7 << 2)) | (value << 2));
                }
                public int this[int i] => (MSB >> i) % 2 * 2 + (LSB >> i) % 2;
                public Rgb Color0 => new Rgb(R * ColorDepth / 256, G * ColorDepth / 256, B * ColorDepth / 256);

                public Rgb Color1
                {
                    get
                    {
                        if (!DiffBit) return new Rgb(R % 16, G % 16, B % 16);
                        var c0 = Color0;
                        int rd = Sign3(R % 8);
                        int gd = Sign3(G % 8);
                        int bd = Sign3(B % 8);
                        return new Rgb(c0.R + rd, c0.G + gd, c0.B + bd);
                    }
                }
            }

            public struct PixelData
            {
                public ulong Alpha { get; set; }
                public Block Block { get; set; }
            }

            public struct Rgb
            {
                public byte R;
                public byte G;
                public byte B;
                public byte Padding;

                public Rgb(int r, int g, int b)
                {
                    R = (byte)r;
                    G = (byte)g;
                    B = (byte)b;
                    Padding = 0;
                }

                public static Rgb operator +(Rgb c, int mod) => new Rgb(Clamp(c.R + mod), Clamp(c.G + mod), Clamp(c.B + mod));
                public static int operator -(Rgb c1, Rgb c2) => ErrorRgb(c1.R - c2.R, c1.G - c2.G, c1.B - c2.B);
                public static Rgb Average(Rgb[] src)
                {
                    int r = 0;
                    int g = 0;
                    int b = 0;
                    for (int i = 0; i < src.Length; i++)
                    {
                        r += src[i].R;
                        g += src[i].G;
                        b += src[i].B;
                    }
                    return new Rgb(r / src.Length, g / src.Length, b / src.Length);
                }
                public Rgb Scale(int limit) => limit == 16 ? new Rgb(R * 17, G * 17, B * 17) : new Rgb((R << 3) | (R >> 2), (G << 3) | (G >> 2), (B << 3) | (B >> 2));
                public Rgb Unscale(int limit) => new Rgb(R * limit / 256, G * limit / 256, B * limit / 256);
                public override int GetHashCode() => R | (G << 8) | (B << 16);
                public override bool Equals(object obj) => obj != null && GetHashCode() == obj.GetHashCode();
                public static bool operator ==(Rgb c1, Rgb c2) => c1.Equals(c2);
                public static bool operator !=(Rgb c1, Rgb c2) => !c1.Equals(c2);
            }

            public class Decoder
            {
                private readonly Queue<Color> queue = new Queue<Color>();
                private readonly bool is3dsOrder;

                public Decoder(bool is3dsOrder)
                {
                    this.is3dsOrder = is3dsOrder;
                }

                public Color Get(Func<PixelData> func)
                {
                    if (queue.Count == 0)
                    {
                        var data = func();
                        var base0 = data.Block.Color0.Scale(data.Block.ColorDepth);
                        var base1 = data.Block.Color1.Scale(data.Block.ColorDepth);
                        int flipMask = data.Block.FlipBit ? 2 : 8;
                        foreach (int i in is3dsOrder ? Order3ds : OrderNormal)
                        {
                            var basec = (i & flipMask) == 0 ? base0 : base1;
                            var mod = Modifiers[(i & flipMask) == 0 ? data.Block.Table0 : data.Block.Table1];
                            var c = basec + mod[data.Block[i]];
                            queue.Enqueue(Color.FromArgb((int)((data.Alpha >> (4 * i)) % 16 * 17), c.R, c.G, c.B));
                        }
                    }
                    return queue.Dequeue();
                }
            }

            public class Encoder
            {
                private static readonly int[] SolidColorLookup = (from limit in new[] { 16, 32 }
                                                                  from inten in Modifiers
                                                                  from selector in inten
                                                                  from color in Enumerable.Range(0, 256)
                                                                  select Enumerable.Range(0, limit).Min(packed =>
                                                                  {
                                                                      int c = (limit == 32) ? (packed << 3) | (packed >> 2) : packed * 17;
                                                                      return (Math.Abs(Clamp(c + selector) - color) << 8) | packed;
                                                                  })).ToArray();

                private readonly List<Color> queue = new List<Color>();
                private readonly bool is3dsOrder;

                public Encoder(bool is3dsOrder)
                {
                    this.is3dsOrder = is3dsOrder;
                }

                public void Set(Color c, Action<PixelData> func)
                {
                    queue.Add(c);
                    if (queue.Count != 16)
                        return;

                    var ordered = new Color[16];
                    var colors = new List<Rgb>(16);
                    for (int j = 0; j < ordered.Length; j++)
                    {
                        ordered[j] = is3dsOrder ? queue[Order3ds[Order3ds[Order3ds[j]]]] : queue[OrderNormal[j]];
                        colors.Add(new Rgb(ordered[j].R, ordered[j].G, ordered[j].B));
                    }

                    ulong alpha = 0;
                    for (int j = ordered.Length - 1; j >= 0; j--)
                        alpha = (alpha * 16) | (byte)(ordered[j].A / 16);

                    Block block;
                    if (colors.All(color => color == colors[0]))
                        block = PackSolidColor(colors[0]);
                    else if (!Optimizer.RepackEtc1CompressedBlock(colors, out block))
                        block = Optimizer.Encode(colors);

                    func(new PixelData { Alpha = alpha, Block = block });
                    queue.Clear();
                }

                private static Block PackSolidColor(Rgb c)
                {
                    int best = 0;
                    int bestError = int.MaxValue;
                    for (int i = 0; i < 64; i++)
                    {
                        int r = SolidColorLookup[i * 256 + c.R];
                        int g = SolidColorLookup[i * 256 + c.G];
                        int b = SolidColorLookup[i * 256 + c.B];
                        int error = ErrorRgb(r >> 8, g >> 8, b >> 8);
                        if (error < bestError)
                        {
                            bestError = error;
                            best = i;
                        }
                    }

                    var soln = new Solution
                    {
                        BlockColor = new Rgb(SolidColorLookup[best * 256 + c.R], SolidColorLookup[best * 256 + c.G], SolidColorLookup[best * 256 + c.B]),
                        IntensityTable = Modifiers[(best >> 2) & 7],
                        SelectorMSB = (best & 2) == 2 ? 0xFF : 0,
                        SelectorLSB = (best & 1) == 1 ? 0xFF : 0
                    };
                    return new SolutionSet(false, (best & 32) == 32, soln, soln).ToBlock();
                }
            }

            private class SolutionSet
            {
                private const int MaxError = 99999999;

                private readonly bool flip;
                private readonly bool diff;
                private Solution soln0;
                private Solution soln1;

                public int TotalError => soln0.Error + soln1.Error;

                public SolutionSet()
                {
                    soln0 = soln1 = new Solution { Error = MaxError };
                }

                public SolutionSet(bool flip, bool diff, Solution soln0, Solution soln1)
                {
                    this.flip = flip;
                    this.diff = diff;
                    this.soln0 = soln0;
                    this.soln1 = soln1;
                }

                public Block ToBlock()
                {
                    var blk = new Block
                    {
                        DiffBit = diff,
                        FlipBit = flip,
                        Table0 = Array.IndexOf(Modifiers, soln0.IntensityTable),
                        Table1 = Array.IndexOf(Modifiers, soln1.IntensityTable)
                    };

                    if (blk.FlipBit)
                    {
                        int m0 = soln0.SelectorMSB;
                        int m1 = soln1.SelectorMSB;
                        m0 = (m0 & 0xC0) * 64 + (m0 & 0x30) * 16 + (m0 & 0xC) * 4 + (m0 & 0x3);
                        m1 = (m1 & 0xC0) * 64 + (m1 & 0x30) * 16 + (m1 & 0xC) * 4 + (m1 & 0x3);
                        blk.MSB = (ushort)(m0 + 4 * m1);

                        int l0 = soln0.SelectorLSB;
                        int l1 = soln1.SelectorLSB;
                        l0 = (l0 & 0xC0) * 64 + (l0 & 0x30) * 16 + (l0 & 0xC) * 4 + (l0 & 0x3);
                        l1 = (l1 & 0xC0) * 64 + (l1 & 0x30) * 16 + (l1 & 0xC) * 4 + (l1 & 0x3);
                        blk.LSB = (ushort)(l0 + 4 * l1);
                    }
                    else
                    {
                        blk.MSB = (ushort)(soln0.SelectorMSB + 256 * soln1.SelectorMSB);
                        blk.LSB = (ushort)(soln0.SelectorLSB + 256 * soln1.SelectorLSB);
                    }

                    if (blk.DiffBit)
                    {
                        int rd = (soln1.BlockColor.R - soln0.BlockColor.R + 8) % 8;
                        int gd = (soln1.BlockColor.G - soln0.BlockColor.G + 8) % 8;
                        int bd = (soln1.BlockColor.B - soln0.BlockColor.B + 8) % 8;
                        blk.R = (byte)(soln0.BlockColor.R * 8 + rd);
                        blk.G = (byte)(soln0.BlockColor.G * 8 + gd);
                        blk.B = (byte)(soln0.BlockColor.B * 8 + bd);
                    }
                    else
                    {
                        blk.R = (byte)(soln0.BlockColor.R * 16 + soln1.BlockColor.R);
                        blk.G = (byte)(soln0.BlockColor.G * 16 + soln1.BlockColor.G);
                        blk.B = (byte)(soln0.BlockColor.B * 16 + soln1.BlockColor.B);
                    }

                    return blk;
                }
            }

            private class Solution
            {
                public int Error;
                public Rgb BlockColor;
                public int[] IntensityTable;
                public int SelectorMSB;
                public int SelectorLSB;
            }

            private class Optimizer
            {
                private readonly Rgb[] pixels;
                private readonly int limit;
                public Rgb BaseColor;
                public Solution BestSolution;

                private static readonly bool[][] Lookup16 = new bool[8][];
                private static readonly bool[][] Lookup32 = new bool[8][];
                private static readonly byte[][][] Lookup16Big = new byte[8][][];
                private static readonly byte[][][] Lookup32Big = new byte[8][][];

                static Optimizer()
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Lookup16[i] = new bool[256];
                        Lookup32[i] = new bool[256];
                        Lookup16Big[i] = new byte[16][];
                        Lookup32Big[i] = new byte[32][];
                        for (int j = 0; j < 16; j++)
                        {
                            Lookup16Big[i][j] = Modifiers[i].Select(mod => (byte)Clamp(j * 17 + mod)).Distinct().ToArray();
                            foreach (var k in Lookup16Big[i][j]) Lookup16[i][k] = true;
                        }
                        for (int j = 0; j < 32; j++)
                        {
                            Lookup32Big[i][j] = Modifiers[i].Select(mod => (byte)Clamp(j * 8 + j / 4 + mod)).Distinct().ToArray();
                            foreach (var k in Lookup32Big[i][j]) Lookup32[i][k] = true;
                        }
                    }
                }

                public Optimizer(Rgb[] pixels, int limit, int error)
                {
                    this.pixels = pixels;
                    this.limit = limit;
                    BaseColor = Rgb.Average(pixels).Unscale(limit);
                    BestSolution = new Solution { Error = error };
                }

                public bool ComputeDeltas(params int[] deltas)
                {
                    bool success = false;
                    foreach (int zd in deltas)
                    {
                        int z = zd + BaseColor.B;
                        if (z < 0 || z >= limit)
                            continue;
                        foreach (int yd in deltas)
                        {
                            int y = yd + BaseColor.G;
                            if (y < 0 || y >= limit)
                                continue;
                            foreach (int xd in deltas)
                            {
                                int x = xd + BaseColor.R;
                                if (x < 0 || x >= limit)
                                    continue;

                                foreach (var t in Modifiers)
                                {
                                    if (EvaluateSolution(new Rgb(x, y, z), t))
                                    {
                                        success = true;
                                        if (BestSolution.Error == 0)
                                            return true;
                                    }
                                }
                            }
                        }
                    }
                    return success;
                }

                private bool TestUnscaledColors(IEnumerable<Rgb> colors)
                {
                    bool success = false;
                    foreach (var c in colors)
                    {
                        foreach (var t in Modifiers)
                        {
                            if (EvaluateSolution(c, t))
                            {
                                success = true;
                                if (BestSolution.Error == 0) return true;
                            }
                        }
                    }
                    return success;
                }

                private IEnumerable<Solution> FindExactMatches(IEnumerable<Rgb> colors, int[] intensityTable)
                {
                    foreach (var c in colors)
                    {
                        BestSolution.Error = 1;
                        if (EvaluateSolution(c, intensityTable))
                            yield return BestSolution;
                    }
                }

                private bool EvaluateSolution(Rgb c, int[] intensityTable)
                {
                    var soln = new Solution { BlockColor = c, IntensityTable = intensityTable };
                    var newTable = new Rgb[4];
                    var scaledColor = c.Scale(limit);
                    for (int i = 0; i < 4; i++)
                        newTable[i] = scaledColor + intensityTable[i];

                    for (int i = 0; i < 8; i++)
                    {
                        int bestJ = 0;
                        int bestError = int.MaxValue;
                        for (int j = 0; j < 4; j++)
                        {
                            int error = pixels[i] - newTable[j];
                            if (error < bestError)
                            {
                                bestError = error;
                                bestJ = j;
                            }
                        }
                        soln.Error += bestError;
                        if (soln.Error >= BestSolution.Error)
                            return false;
                        soln.SelectorMSB |= (byte)(bestJ / 2 << i);
                        soln.SelectorLSB |= (byte)(bestJ % 2 << i);
                    }

                    BestSolution = soln;
                    return true;
                }

                public static bool RepackEtc1CompressedBlock(List<Rgb> colors, out Block block)
                {
                    for (int flipIndex = 0; flipIndex < 2; flipIndex++)
                    {
                        bool flip = flipIndex == 1;
                        var px = SplitColors(colors, flip);
                        var all0 = px[0];
                        var p0 = DistinctSmall(all0);
                        if (p0.Length > 4) continue;

                        var all1 = px[1];
                        var p1 = DistinctSmall(all1);
                        if (p1.Length > 4) continue;

                        for (int diffIndex = 0; diffIndex < 2; diffIndex++)
                        {
                            bool diff = diffIndex == 1;
                            if (!diff)
                            {
                                var tables0 = GetTables(p0, Lookup16);
                                if (tables0.Count == 0) continue;
                                var tables1 = GetTables(p1, Lookup16);
                                if (tables1.Count == 0) continue;

                                var opt0 = new Optimizer(all0, 16, 1);
                                Solution soln0 = null;
                                foreach (var ti in tables0)
                                {
                                    soln0 = opt0.FindExactMatch(GetCandidates(p0, Lookup16Big[ti], 16, 0), GetCandidates(p0, Lookup16Big[ti], 16, 1), GetCandidates(p0, Lookup16Big[ti], 16, 2), Modifiers[ti]);
                                    if (soln0 != null) break;
                                }
                                if (soln0 == null) continue;

                                var opt1 = new Optimizer(all1, 16, 1);
                                foreach (var ti in tables1)
                                {
                                    var soln1 = opt1.FindExactMatch(GetCandidates(p1, Lookup16Big[ti], 16, 0), GetCandidates(p1, Lookup16Big[ti], 16, 1), GetCandidates(p1, Lookup16Big[ti], 16, 2), Modifiers[ti]);
                                    if (soln1 != null)
                                    {
                                        block = new SolutionSet(flip, diff, soln0, soln1).ToBlock();
                                        return true;
                                    }
                                }
                            }
                            else
                            {
                                var tables0 = GetTables(p0, Lookup32);
                                if (tables0.Count == 0) continue;
                                var tables1 = GetTables(p1, Lookup32);
                                if (tables1.Count == 0) continue;

                                var opt0 = new Optimizer(all0, 32, 1);
                                var solns0 = new List<Solution>();
                                foreach (var ti in tables0)
                                {
                                    opt0.AddExactMatches(solns0, GetCandidates(p0, Lookup32Big[ti], 32, 0), GetCandidates(p0, Lookup32Big[ti], 32, 1), GetCandidates(p0, Lookup32Big[ti], 32, 2), Modifiers[ti]);
                                }
                                if (solns0.Count == 0) continue;

                                var opt1 = new Optimizer(all1, 32, 1);
                                foreach (var ti in tables1)
                                {
                                    var rs = GetCandidates(p1, Lookup32Big[ti], 32, 0);
                                    var gs = GetCandidates(p1, Lookup32Big[ti], 32, 1);
                                    var bs = GetCandidates(p1, Lookup32Big[ti], 32, 2);
                                    foreach (var soln0 in solns0)
                                    {
                                        var soln1 = opt1.FindDiffExactMatch(rs, gs, bs, soln0.BlockColor, Modifiers[ti]);
                                        if (soln1 != null)
                                        {
                                            block = new SolutionSet(flip, diff, soln0, soln1).ToBlock();
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    block = default(Block);
                    return false;
                }

                private Solution FindExactMatch(List<int> rs, List<int> gs, List<int> bs, int[] intensityTable)
                {
                    for (int r = 0; r < rs.Count; r++)
                        for (int g = 0; g < gs.Count; g++)
                            for (int b = 0; b < bs.Count; b++)
                            {
                                BestSolution.Error = 1;
                                if (EvaluateSolution(new Rgb(rs[r], gs[g], bs[b]), intensityTable))
                                    return BestSolution;
                            }
                    return null;
                }

                private void AddExactMatches(List<Solution> solutions, List<int> rs, List<int> gs, List<int> bs, int[] intensityTable)
                {
                    for (int r = 0; r < rs.Count; r++)
                        for (int g = 0; g < gs.Count; g++)
                            for (int b = 0; b < bs.Count; b++)
                            {
                                BestSolution.Error = 1;
                                if (EvaluateSolution(new Rgb(rs[r], gs[g], bs[b]), intensityTable))
                                    solutions.Add(BestSolution);
                            }
                }

                private Solution FindDiffExactMatch(List<int> rs, List<int> gs, List<int> bs, Rgb baseColor, int[] intensityTable)
                {
                    for (int r = 0; r < rs.Count; r++)
                    {
                        int dr = rs[r] - baseColor.R;
                        if (dr < -4 || dr >= 4)
                            continue;
                        for (int g = 0; g < gs.Count; g++)
                        {
                            int dg = gs[g] - baseColor.G;
                            if (dg < -4 || dg >= 4)
                                continue;
                            for (int b = 0; b < bs.Count; b++)
                            {
                                int db = bs[b] - baseColor.B;
                                if (db < -4 || db >= 4)
                                    continue;

                                BestSolution.Error = 1;
                                if (EvaluateSolution(new Rgb(rs[r], gs[g], bs[b]), intensityTable))
                                    return BestSolution;
                            }
                        }
                    }
                    return null;
                }

                public static Block Encode(List<Rgb> colors)
                {
                    var best = new SolutionSet();
                    for (int flipIndex = 0; flipIndex < 2; flipIndex++)
                    {
                        bool flip = flipIndex == 1;
                        var px = SplitColors(colors, flip);
                        for (int diffIndex = 0; diffIndex < 2; diffIndex++)
                        {
                            bool diff = diffIndex == 1;
                            var solns = new Solution[2];
                            int limit = diff ? 32 : 16;
                            int i;
                            for (i = 0; i < 2; i++)
                            {
                                int threshold = best.TotalError;
                                if (i == 1) threshold -= solns[0].Error;
                                var opt = new Optimizer(px[i], limit, threshold);
                                if (i == 1 && diff)
                                {
                                    opt.BaseColor = solns[0].BlockColor;
                                    if (!opt.ComputeDeltas(-4, -3, -2, -1, 0, 1, 2, 3))
                                        break;
                                }
                                else
                                {
                                    if (!opt.ComputeDeltas(-4, -3, -2, -1, 0, 1, 2, 3, 4))
                                        break;
                                    if (opt.BestSolution.Error > 9000)
                                    {
                                        if (opt.BestSolution.Error > 18000)
                                            opt.ComputeDeltas(-8, -7, -6, -5, 5, 6, 7, 8);
                                        else
                                            opt.ComputeDeltas(-5, 5);
                                    }
                                }

                                if (opt.BestSolution.Error >= threshold)
                                    break;
                                solns[i] = opt.BestSolution;
                            }

                            if (i == 2)
                            {
                                var set = new SolutionSet(flip, diff, solns[0], solns[1]);
                                if (set.TotalError < best.TotalError)
                                    best = set;
                            }
                        }
                    }
                    return best.ToBlock();
                }

                private static Rgb[][] SplitColors(List<Rgb> colors, bool flip)
                {
                    var px = new[] { new Rgb[8], new Rgb[8] };
                    var counts = new int[2];
                    int divisor = flip ? 2 : 8;
                    for (int j = 0; j < colors.Count; j++)
                    {
                        int group = (j / divisor) % 2;
                        px[group][counts[group]++] = colors[j];
                    }
                    return px;
                }

                private static Rgb[] DistinctSmall(Rgb[] colors)
                {
                    var distinct = new List<Rgb>(colors.Length);
                    for (int i = 0; i < colors.Length; i++)
                    {
                        bool exists = false;
                        for (int j = 0; j < distinct.Count; j++)
                        {
                            if (distinct[j] == colors[i])
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                            distinct.Add(colors[i]);
                    }
                    return distinct.ToArray();
                }

                private static List<int> GetTables(Rgb[] colors, bool[][] lookup)
                {
                    var tables = new List<int>(8);
                    for (int i = 0; i < 8; i++)
                    {
                        bool ok = true;
                        for (int j = 0; j < colors.Length; j++)
                        {
                            if (!lookup[i][colors[j].R] || !lookup[i][colors[j].G] || !lookup[i][colors[j].B])
                            {
                                ok = false;
                                break;
                            }
                        }
                        if (ok)
                            tables.Add(i);
                    }
                    return tables;
                }

                private static List<int> GetCandidates(Rgb[] colors, byte[][] lookup, int limit, int channel)
                {
                    var candidates = new List<int>(limit);
                    for (int i = 0; i < limit; i++)
                    {
                        bool ok = true;
                        for (int j = 0; j < colors.Length; j++)
                        {
                            byte value = channel == 0 ? colors[j].R : channel == 1 ? colors[j].G : colors[j].B;
                            if (!Contains(lookup[i], value))
                            {
                                ok = false;
                                break;
                            }
                        }
                        if (ok)
                            candidates.Add(i);
                    }
                    return candidates;
                }

                private static bool Contains(byte[] values, byte value)
                {
                    for (int i = 0; i < values.Length; i++)
                        if (values[i] == value)
                            return true;
                    return false;
                }
            }

            private static int Clamp(int n) => Math.Max(0, Math.Min(n, 255));
            private static int Sign3(int n) => (n + 4) % 8 - 4;
            private static int ErrorRgb(int r, int g, int b) => 2 * r * r + 4 * g * g + 3 * b * b;
        }
    }
}
