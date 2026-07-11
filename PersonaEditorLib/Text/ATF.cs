using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditorLib.Text
{
    public class ATF : IGameData
    {
        public const uint MagicNumber = 0x00465441;
        private const int HeaderSize = 0x50;
        private const int EntrySize = 0x20;

        private readonly List<ATFEntry> entries = new List<ATFEntry>();
        private ATFHeader header;
        private byte[] data;

        public ATF(byte[] data)
        {
            this.data = data?.ToArray() ?? throw new ArgumentNullException(nameof(data));
            Read();
        }

        public IReadOnlyList<ATFEntry> Entries => entries;

        public FormatEnum Type => FormatEnum.ATF;
        public List<GameFile> SubFiles { get; } = new List<GameFile>();
        public int GetSize()
        {
            int textLength = 0;
            foreach (var entry in entries)
            {
                string text = string.IsNullOrEmpty(entry.NewText) ? entry.OldText : entry.NewText;
                textLength += (text.Length + 1) * 2;
            }
            return checked((int)header.TextTableOffset + textLength);
        }
        public byte[] GetData() => BuildData();

        public string[] ExportText(string fileName, bool removeSplit)
        {
            return entries.Select((entry, index) =>
                $"{fileName}\t{index}\t{EscapeText(entry.OldText, removeSplit)}\t").ToArray();
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

        private void Read()
        {
            if (data.Length < HeaderSize)
                throw new Exception("ATF: file too small");

            using MemoryStream ms = new MemoryStream(data);
            using BinaryReader br = new BinaryReader(ms, Encoding.Unicode, true);

            header = ReadHeader(br);
            if (header.Signature != MagicNumber)
                throw new Exception("ATF: wrong magic number");

            long entryTableOffset = header.ByteCodeStart + header.ByteCodeLength;
            if (entryTableOffset < 0 || entryTableOffset + header.Count * EntrySize > data.Length)
                throw new Exception("ATF: invalid entry table offset");

            br.BaseStream.Position = entryTableOffset;
            entries.Clear();

            for (int i = 0; i < header.Count; i++)
            {
                ATFEntry entry = ReadEntry(br);
                entry.OldText = ReadUnicodeCString(data, checked((int)(header.TextTableOffset + entry.TextPos * 2)));
                entry.NewText = string.Empty;
                entries.Add(entry);
            }
        }

        private byte[] BuildData()
        {
            byte[] headerData = new byte[header.TextTableOffset];
            Buffer.BlockCopy(data, 0, headerData, 0, headerData.Length);

            byte[] stringTable = BuildStringTable(out uint textUniLength);
            header.TextUniTableLength = textUniLength;
            header.TextTableLength = (uint)stringTable.Length;

            using MemoryStream ms = new MemoryStream();
            ms.Write(headerData, 0, headerData.Length);

            WriteHeader(ms, header);

            long entryTableOffset = header.ByteCodeStart + header.ByteCodeLength;
            ms.Position = entryTableOffset;
            foreach (var entry in entries)
                WriteEntry(ms, entry);

            ms.Position = header.TextTableOffset;
            ms.Write(stringTable, 0, stringTable.Length);
            return ms.ToArray();
        }

        private byte[] BuildStringTable(out uint textUniLength)
        {
            int length = 0;
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter bw = new BinaryWriter(ms, Encoding.Unicode, true);

            foreach (var entry in entries)
            {
                string text = string.IsNullOrEmpty(entry.NewText) ? entry.OldText : entry.NewText;
                entry.TextPos = (uint)length;
                entry.TextUniLength = (uint)text.Length;

                bw.Write(Encoding.Unicode.GetBytes(text));
                bw.Write((ushort)0);
                length += text.Length + 1;
            }

            textUniLength = (uint)length;
            return ms.ToArray();
        }

        private ATFHeader ReadHeader(BinaryReader br)
        {
            return new ATFHeader
            {
                Signature = br.ReadUInt32(),
                Count = br.ReadUInt32(),
                Unk2 = br.ReadInt32(),
                Unk3 = br.ReadInt32(),
                ByteCodeStart = br.ReadUInt32(),
                Count2 = br.ReadInt32(),
                ByteCodeLength = br.ReadUInt32(),
                Unk6 = br.ReadInt32(),
                Unk7 = br.ReadInt32(),
                Unk8 = br.ReadInt32(),
                Unk9 = br.ReadInt32(),
                Unk10 = br.ReadInt32(),
                StringsTableOffset = br.ReadUInt32(),
                StringsTableLength = br.ReadUInt32(),
                Unk11 = br.ReadInt32(),
                Unk12 = br.ReadInt32(),
                TextTableOffset = br.ReadUInt32(),
                TextUniTableLength = br.ReadUInt32(),
                TextTableLength = br.ReadUInt32(),
                Unk14 = br.ReadInt32()
            };
        }

        private void WriteHeader(MemoryStream ms, ATFHeader header)
        {
            long position = ms.Position;
            ms.Position = 0;
            using BinaryWriter bw = new BinaryWriter(ms, Encoding.Unicode, true);
            bw.Write(header.Signature);
            bw.Write(header.Count);
            bw.Write(header.Unk2);
            bw.Write(header.Unk3);
            bw.Write(header.ByteCodeStart);
            bw.Write(header.Count2);
            bw.Write(header.ByteCodeLength);
            bw.Write(header.Unk6);
            bw.Write(header.Unk7);
            bw.Write(header.Unk8);
            bw.Write(header.Unk9);
            bw.Write(header.Unk10);
            bw.Write(header.StringsTableOffset);
            bw.Write(header.StringsTableLength);
            bw.Write(header.Unk11);
            bw.Write(header.Unk12);
            bw.Write(header.TextTableOffset);
            bw.Write(header.TextUniTableLength);
            bw.Write(header.TextTableLength);
            bw.Write(header.Unk14);
            ms.Position = position;
        }

        private ATFEntry ReadEntry(BinaryReader br)
        {
            return new ATFEntry
            {
                StringPosition = br.ReadInt32(),
                StringLength = br.ReadInt32(),
                Dummy1 = br.ReadInt64(),
                TextPos = br.ReadUInt32(),
                TextUniLength = br.ReadUInt32(),
                Dummy2 = br.ReadInt64()
            };
        }

        private void WriteEntry(MemoryStream ms, ATFEntry entry)
        {
            using BinaryWriter bw = new BinaryWriter(ms, Encoding.Unicode, true);
            bw.Write(entry.StringPosition);
            bw.Write(entry.StringLength);
            bw.Write(entry.Dummy1);
            bw.Write(entry.TextPos);
            bw.Write(entry.TextUniLength);
            bw.Write(entry.Dummy2);
        }

        private string ReadUnicodeCString(byte[] buffer, int offset)
        {
            if (offset < 0 || offset >= buffer.Length)
                return string.Empty;

            int end = offset;
            for (; end + 1 < buffer.Length; end += 2)
            {
                if (buffer[end] == 0 && buffer[end + 1] == 0)
                    break;
            }

            return end <= offset ? string.Empty : Encoding.Unicode.GetString(buffer, offset, end - offset);
        }

        private string EscapeText(string text, bool removeSplit)
        {
            text ??= string.Empty;
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return removeSplit ? text.Replace("\n", " ") : text.Replace("\n", "\\n");
        }

        public class ATFEntry
        {
            public int StringPosition { get; set; }
            public int StringLength { get; set; }
            public long Dummy1 { get; set; }
            public uint TextPos { get; set; }
            public uint TextUniLength { get; set; }
            public long Dummy2 { get; set; }
            public string OldText { get; set; }
            public string NewText { get; set; }
        }

        private class ATFHeader
        {
            public uint Signature;
            public uint Count;
            public int Unk2;
            public int Unk3;
            public uint ByteCodeStart;
            public int Count2;
            public uint ByteCodeLength;
            public int Unk6;
            public int Unk7;
            public int Unk8;
            public int Unk9;
            public int Unk10;
            public uint StringsTableOffset;
            public uint StringsTableLength;
            public int Unk11;
            public int Unk12;
            public uint TextTableOffset;
            public uint TextUniTableLength;
            public uint TextTableLength;
            public int Unk14;
        }
    }
}
