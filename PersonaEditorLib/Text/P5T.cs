using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditorLib.Text
{
    public class P5T : IGameData
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly byte[] originalData;
        private readonly List<P5TEntry> entries = new List<P5TEntry>();

        public P5T(byte[] data)
        {
            originalData = data?.ToArray() ?? throw new ArgumentNullException(nameof(data));
            Read(originalData);
        }

        public IReadOnlyList<P5TEntry> Entries => entries;
        public FormatEnum Type => FormatEnum.P5T;
        public List<GameFile> SubFiles { get; } = new List<GameFile>();
        public int GetSize() => GetData().Length;

        public byte[] GetData()
        {
            if (entries.All(x => !x.HasChanges))
                return originalData.ToArray();

            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(entries.Count);
            foreach (P5TEntry entry in entries)
                entry.Write(writer, Utf8);

            return stream.ToArray();
        }

        public string[] ExportText(string fileName, bool removeSplit)
        {
            return entries.Select(entry =>
                $"{fileName}\t{entry.Index}\t{EscapeText(entry.Key, removeSplit)}\t"
                + $"{EscapeText(entry.Identifier, removeSplit)}\t{EscapeText(entry.OldText, removeSplit)}\t")
                .ToArray();
        }

        public void ImportText(IEnumerable<(int Index, string Key, string Identifier, string Text)> importedText)
        {
            if (importedText == null)
                return;

            Dictionary<int, P5TEntry> byIndex = entries.ToDictionary(x => x.Index);
            foreach (var item in importedText)
            {
                if (!byIndex.TryGetValue(item.Index, out P5TEntry entry))
                    continue;
                if (!string.IsNullOrEmpty(item.Key) && !string.Equals(item.Key, entry.Key, StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrEmpty(item.Identifier)
                    && !string.Equals(item.Identifier, entry.Identifier, StringComparison.Ordinal))
                    continue;

                entry.NewText = PrepareImportedText(item.Text);
            }
        }

        public void ImportTextByIndex(IEnumerable<(int Index, string Text)> importedText)
        {
            if (importedText == null)
                return;

            Dictionary<int, P5TEntry> byIndex = entries.ToDictionary(x => x.Index);
            foreach (var item in importedText)
                if (byIndex.TryGetValue(item.Index, out P5TEntry entry))
                    entry.NewText = PrepareImportedText(item.Text);
        }

        public static bool IsP5T(byte[] data)
        {
            try
            {
                new P5T(data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Read(byte[] data)
        {
            if (data.Length < 4)
                throw new InvalidDataException("P5T: file is too small");

            int position = 0;
            uint rawCount = ReadUInt32(data, ref position, "entry count");
            if (rawCount > int.MaxValue || rawCount > data.Length)
                throw new InvalidDataException("P5T: invalid entry count");

            entries.Clear();
            for (int index = 0; index < (int)rawCount; index++)
            {
                int offset = position;
                try
                {
                    string key = ReadString(data, ref position, "key");
                    uint value = ReadUInt32(data, ref position, "value");
                    string identifier = ReadString(data, ref position, "identifier");
                    string text = ReadString(data, ref position, "text");
                    uint trailerA = ReadUInt32(data, ref position, "trailer A");
                    uint trailerB = ReadUInt32(data, ref position, "trailer B");
                    uint trailerC = ReadUInt32(data, ref position, "trailer C");

                    entries.Add(new P5TEntry(index, key, value, identifier, text, trailerA, trailerB, trailerC));
                }
                catch (Exception exception) when (exception is InvalidDataException || exception is DecoderFallbackException)
                {
                    throw new InvalidDataException($"P5T: invalid entry {index} at 0x{offset:X}", exception);
                }
            }

            if (position != data.Length)
                throw new InvalidDataException("P5T: trailing data after the last entry");
        }

        private static string ReadString(byte[] data, ref int position, string label)
        {
            uint length = ReadVarUInt(data, ref position, label + " length");
            if (length > int.MaxValue || position > data.Length - (int)length)
                throw new InvalidDataException($"P5T: invalid {label} length");

            string result = Utf8.GetString(data, position, (int)length);
            position += (int)length;
            return result;
        }

        private static uint ReadVarUInt(byte[] data, ref int position, string label)
        {
            uint result = 0;
            for (int i = 0; i < 5; i++)
            {
                if (position >= data.Length)
                    throw new InvalidDataException($"P5T: unexpected EOF reading {label}");

                byte value = data[position++];
                if (i == 4 && (value & 0x7F) > 0x0F)
                    throw new InvalidDataException($"P5T: overlong {label}");

                result |= (uint)(value & 0x7F) << (i * 7);
                if ((value & 0x80) == 0)
                    return result;
            }

            throw new InvalidDataException($"P5T: overlong {label}");
        }

        private static uint ReadUInt32(byte[] data, ref int position, string label)
        {
            if (position > data.Length - 4)
                throw new InvalidDataException($"P5T: unexpected EOF reading {label}");

            uint result = (uint)(data[position]
                | data[position + 1] << 8
                | data[position + 2] << 16
                | data[position + 3] << 24);
            position += 4;
            return result;
        }

        private static void WriteVarUInt(BinaryWriter writer, uint value)
        {
            while (value >= 0x80)
            {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }

            writer.Write((byte)value);
        }

        private static string EscapeText(string text, bool removeSplit)
        {
            text = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            return removeSplit ? text.Replace("\n", " ") : text.Replace("\n", "\\n");
        }

        private static string PrepareImportedText(string text)
        {
            return (text ?? string.Empty).Replace("\\n", "\n");
        }

        public class P5TEntry
        {
            private string newText;

            internal P5TEntry(int index, string key, uint value, string identifier, string text,
                uint trailerA, uint trailerB, uint trailerC)
            {
                Index = index;
                Key = key;
                Value = value;
                Identifier = identifier;
                OldText = text;
                TrailerA = trailerA;
                TrailerB = trailerB;
                TrailerCBits = trailerC;
            }

            public int Index { get; }
            public string Key { get; }
            public uint Value { get; }
            public string Identifier { get; }
            public string OldText { get; }
            public string NewText
            {
                get { return newText; }
                set { newText = value; }
            }
            public string CurrentText => newText ?? OldText;
            public uint TrailerA { get; }
            public uint TrailerB { get; }
            public uint TrailerCBits { get; }
            public float TrailerC => BitConverter.ToSingle(BitConverter.GetBytes(TrailerCBits), 0);
            public bool HasChanges => newText != null;

            internal void Write(BinaryWriter writer, Encoding encoding)
            {
                byte[] key = encoding.GetBytes(Key);
                byte[] identifier = encoding.GetBytes(Identifier);
                byte[] text = encoding.GetBytes(CurrentText);

                WriteVarUInt(writer, checked((uint)key.Length));
                writer.Write(key);
                writer.Write(Value);
                WriteVarUInt(writer, checked((uint)identifier.Length));
                writer.Write(identifier);
                WriteVarUInt(writer, checked((uint)text.Length));
                writer.Write(text);
                writer.Write(TrailerA);
                writer.Write(TrailerB);
                writer.Write(TrailerCBits);
            }
        }
    }
}
