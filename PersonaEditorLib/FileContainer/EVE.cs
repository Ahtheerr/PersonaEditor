using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditorLib.FileContainer
{
    /// <summary>
    /// A Soul Hackers event script. Textbox contents are exposed as editable
    /// text while the surrounding event commands remain in the expression.
    /// </summary>
    public class EVE : IGameData
    {
        private const int HeaderSize = 8;
        private static readonly byte[] TextboxSeparator = { 0xFF, 0x03, 0xFF, 0x02 };
        private static readonly byte[] SpeakerPrefix = { 0xFF, 0x19, 0xC7, 0xB7 };
        private static readonly byte[] SpeakerDelimiter = { 0xC7, 0xB8, 0xFF, 0x16 };
        private readonly ushort[] pointers;
        private readonly byte[] originalData;
        private readonly int logicalTextEnd;
        private readonly List<EVEChunk> chunks = new List<EVEChunk>();
        private int nextStringIndex;

        public byte[] Data { get; private set; }

        public ushort FormatVersion { get; }
        public ushort CodeTableEnd { get; }
        public ushort StringTableStart { get; }
        public ushort TextStart { get; }

        public List<EVEString> Strings { get; } = new List<EVEString>();
        public List<GameFile> SubFiles { get; } = new List<GameFile>();

        public bool HasChanges => chunks.Any(x => x.HasChanges);

        public EVE(byte[] data)
        {
            if (!TryReadHeader(data, 0, out ushort formatVersion, out ushort codeTableEnd,
                out ushort stringTableStart, out ushort textStart))
                throw new InvalidDataException("EVE: invalid event header.");
            if ((textStart - stringTableStart) % 2 != 0)
                throw new InvalidDataException("EVE: string table has an odd size.");

            Data = data;
            originalData = (byte[])data.Clone();
            FormatVersion = formatVersion;
            CodeTableEnd = codeTableEnd;
            StringTableStart = stringTableStart;
            TextStart = textStart;

            int count = (TextStart - StringTableStart) / 2;
            pointers = new ushort[count];
            for (int i = 0; i < count; i++)
                pointers[i] = ReadUInt16(Data, StringTableStart + i * 2);

            logicalTextEnd = GetLogicalTextEnd(Data, TextStart);
            int textSize = logicalTextEnd - TextStart;
            List<int> starts = pointers.Where(x => x <= textSize).Select(x => (int)x).Distinct().OrderBy(x => x).ToList();
            Dictionary<int, EVEChunk> chunksByOffset = new Dictionary<int, EVEChunk>();
            for (int i = 0; i < starts.Count; i++)
            {
                int start = starts[i];
                int end = i + 1 < starts.Count ? starts[i + 1] : textSize;
                if (end <= start)
                    continue;

                EVEChunk chunk = new EVEChunk(start);
                AddChunkEntries(chunk, start, end);
                chunks.Add(chunk);
                chunksByOffset.Add(start, chunk);
            }

            var addedOffsets = new HashSet<int>();
            for (int pointerIndex = 0; pointerIndex < pointers.Length; pointerIndex++)
            {
                ushort pointer = pointers[pointerIndex];
                if (pointer >= textSize)
                {
                    AddInvalidEntry(pointer, "<offset outside text>");
                    continue;
                }

                if (IsNonIndependentPointer(pointerIndex, textSize)
                    || !addedOffsets.Add(pointer)
                    || !chunksByOffset.TryGetValue(pointer, out EVEChunk chunk))
                {
                    AddInvalidEntry(pointer, "<non-monotonic/shared offset>");
                    continue;
                }

                foreach (EVEString entry in chunk.Entries)
                    Strings.Add(entry);
            }
        }

        private bool IsNonIndependentPointer(int pointerIndex, int textSize)
        {
            if (pointerIndex + 1 >= pointers.Length)
                return false;

            int start = pointers[pointerIndex];
            int end = pointers[pointerIndex + 1];
            return end <= start || end > textSize;
        }

        public static bool IsEve(byte[] data)
            => TryReadHeader(data, 0, out _, out _, out _, out _);

        internal static bool TryReadHeader(byte[] data, int offset, out ushort formatVersion,
            out ushort codeTableEnd, out ushort stringTableStart, out ushort textStart)
        {
            formatVersion = 0;
            codeTableEnd = 0;
            stringTableStart = 0;
            textStart = 0;

            if (data == null || offset < 0 || offset > data.Length - HeaderSize)
                return false;

            formatVersion = ReadUInt16(data, offset);
            codeTableEnd = ReadUInt16(data, offset + 2);
            stringTableStart = ReadUInt16(data, offset + 4);
            textStart = ReadUInt16(data, offset + 6);
            int size = data.Length - offset;

            return formatVersion == 8
                && codeTableEnd >= HeaderSize
                && codeTableEnd <= stringTableStart
                && stringTableStart <= textStart
                && textStart <= size
                && (codeTableEnd & 1) == 0
                && (stringTableStart & 1) == 0
                && (textStart & 1) == 0;
        }

        public void DiscardChanges()
        {
            foreach (EVEString entry in Strings)
                entry.Reset();
        }

        public string[] ExportText(string fileName, bool removeSplit)
        {
            return Strings.Where(x => x.IsEditable && x.Kind == EVEStringKind.Text)
                .Select(x => new
                {
                    Entry = x,
                    Speaker = FormatExportText(x.Speaker?.Text, removeSplit),
                    Text = FormatExportText(x.Text, removeSplit)
                })
                .Where(x => !string.IsNullOrEmpty(x.Text))
                .Select(x => $"{fileName}\t{x.Entry.Index}\t{x.Speaker}\t{x.Text}\t")
                .ToArray();
        }

        public void ImportText(IEnumerable<(int Index, string Text)> importedText)
        {
            if (importedText == null)
                return;

            Dictionary<int, EVEString> byIndex = Strings.ToDictionary(x => x.Index);
            foreach ((int Index, string Text) item in importedText)
                if (byIndex.TryGetValue(item.Index, out EVEString entry) && entry.IsEditable)
                    entry.Text = item.Text ?? string.Empty;
        }

        public void ImportTextRows(IEnumerable<(int Index, string Speaker, string Text)> importedText)
        {
            if (importedText == null)
                return;

            Dictionary<int, EVEString> byIndex = Strings.ToDictionary(x => x.Index);
            foreach ((int Index, string Speaker, string Text) item in importedText)
            {
                if (!byIndex.TryGetValue(item.Index, out EVEString entry) || !entry.IsEditable
                    || entry.Kind != EVEStringKind.Text)
                    continue;

                if (!string.IsNullOrEmpty(item.Speaker) && entry.Speaker?.IsEditable == true)
                    entry.Speaker.Text = item.Speaker;
                if (!string.IsNullOrEmpty(item.Text))
                    entry.Text = item.Text;
            }
        }

        public FormatEnum Type => FormatEnum.EVE;

        public List<GameFile> GetSubFiles() => SubFiles;

        public int GetSize() => GetData().Length;

        public byte[] GetData()
        {
            if (!HasChanges)
                return Data;

            Dictionary<int, byte[]> replacements = new Dictionary<int, byte[]>();
            foreach (EVEChunk chunk in chunks)
                if (chunk.HasChanges)
                    replacements[chunk.Offset] = chunk.GetData();

            int textSize = logicalTextEnd - TextStart;
            List<int> starts = pointers.Where(x => x <= textSize).Select(x => (int)x).Distinct().OrderBy(x => x).ToList();
            var output = new List<byte>(Math.Max(originalData.Length, TextStart + textSize));
            for (int i = 0; i < StringTableStart; i++)
                output.Add(originalData[i]);

            Dictionary<int, int> newOffsets = new Dictionary<int, int>();
            int cursor = 0;
            var textChunks = new List<byte[]>();
            for (int i = 0; i < starts.Count; i++)
            {
                int start = starts[i];
                int end = i + 1 < starts.Count ? starts[i + 1] : textSize;
                byte[] chunk = replacements.TryGetValue(start, out byte[] changed)
                    ? changed
                    : CopyBytes(originalData, TextStart + start, end - start);

                newOffsets[start] = cursor;
                textChunks.Add(chunk);
                cursor = checked(cursor + chunk.Length);
            }

            foreach (ushort pointer in pointers)
            {
                int newOffset = newOffsets.TryGetValue(pointer, out int remapped) ? remapped : pointer;
                if (newOffset > ushort.MaxValue)
                    throw new InvalidDataException("EVE: rebuilt string table is too large.");
                output.Add((byte)(newOffset >> 8));
                output.Add((byte)newOffset);
            }

            foreach (byte[] chunk in textChunks)
                output.AddRange(chunk);
            for (int i = logicalTextEnd; i < originalData.Length; i++)
                output.Add(originalData[i]);

            while (output.Count < originalData.Length)
                output.Add(0);
            return output.ToArray();
        }

        private void AddChunkEntries(EVEChunk chunk, int start, int end)
        {
            int segmentStart = start;
            for (int position = start; position + 3 < end; position++)
            {
                if (originalData[TextStart + position] != 0xFF
                    || originalData[TextStart + position + 1] != 0x03
                    || originalData[TextStart + position + 2] != 0xFF
                    || originalData[TextStart + position + 3] != 0x02)
                    continue;

                AddSegment(chunk, segmentStart, position);
                chunk.Separators.Add(TextboxSeparator);
                segmentStart = position + 4;
                position += 3;
            }

            AddSegment(chunk, segmentStart, end);
        }

        private void AddSegment(EVEChunk chunk, int start, int end)
        {
            if (TrySplitSpeaker(start, end, out int prefixStart, out int speakerStart,
                out int speakerEnd, out int textStart))
            {
                byte[] prefix = Combine(CopyBytes(originalData, TextStart + start, prefixStart - start), SpeakerPrefix);
                EVEString speaker = AddEntry(chunk, speakerStart, speakerEnd, EVEStringKind.Speaker, prefix, SpeakerDelimiter);
                EVEString text = AddEntry(chunk, textStart, end, EVEStringKind.Text, null, null);
                text.Speaker = speaker;
                return;
            }

            AddEntry(chunk, start, end, EVEStringKind.Text, null, null);
        }

        private bool TrySplitSpeaker(int start, int end, out int prefixStart, out int speakerStart,
            out int speakerEnd, out int textStart)
        {
            prefixStart = start;
            speakerStart = 0;
            speakerEnd = 0;
            textStart = 0;
            while (prefixStart < end && !Matches(TextStart + prefixStart, SpeakerPrefix))
            {
                byte value = originalData[TextStart + prefixStart];
                if (IsTextByte(value) || value == 0x0A || value == 0x0D)
                    return false;

                if (value == 0xFF)
                {
                    int commandLength = GetCommandLength(originalData, TextStart + prefixStart, TextStart + end);
                    prefixStart += commandLength;
                    if (commandLength == 2 && prefixStart - 1 < end
                        && originalData[TextStart + prefixStart - 1] == 0x05)
                        prefixStart += Math.Min(2, end - prefixStart);
                }
                else if (value == 0x0F)
                    prefixStart += Math.Min(2, end - prefixStart);
                else
                    prefixStart++;
            }

            if (prefixStart > end - SpeakerPrefix.Length)
                return false;

            for (int position = prefixStart + SpeakerPrefix.Length; position <= end - SpeakerDelimiter.Length; position++)
            {
                if (!Matches(TextStart + position, SpeakerDelimiter))
                    continue;

                speakerStart = prefixStart + SpeakerPrefix.Length;
                speakerEnd = position;
                textStart = position + SpeakerDelimiter.Length;
                return true;
            }

            return false;
        }

        private bool Matches(int offset, byte[] value)
        {
            if (offset < 0 || offset > originalData.Length - value.Length)
                return false;

            for (int index = 0; index < value.Length; index++)
                if (originalData[offset + index] != value[index])
                    return false;

            return true;
        }

        private EVEString AddEntry(EVEChunk chunk, int start, int end, EVEStringKind kind, byte[] prefix, byte[] suffix)
        {
            GetEditableRange(start, end, out int editableStart, out int editableEnd);
            byte[] leading = Combine(prefix, CopyBytes(originalData, TextStart + start, editableStart - start));
            byte[] trailing = Combine(CopyBytes(originalData, TextStart + editableEnd, end - editableEnd), suffix);
            string text = Escape(originalData, TextStart + editableStart, editableEnd - editableStart);
            var entry = new EVEString(nextStringIndex++, editableStart, text, true, kind, leading, trailing);
            chunk.Entries.Add(entry);
            return entry;
        }

        private void GetEditableRange(int start, int end, out int editableStart, out int editableEnd)
        {
            editableStart = -1;
            editableEnd = -1;

            for (int position = start; position < end;)
            {
                byte value = originalData[TextStart + position];
                if (value == 0xFF)
                {
                    int commandLength = GetCommandLength(originalData, TextStart + position, TextStart + end);
                    position += commandLength;
                    if (commandLength == 2 && position - 1 < end && originalData[TextStart + position - 1] == 0x05)
                        position += Math.Min(2, end - position);
                    continue;
                }

                if (value == 0x0F)
                {
                    position += Math.Min(2, end - position);
                    continue;
                }

                if (IsTextByte(value) || value == 0x0A || value == 0x0D)
                {
                    if (editableStart < 0)
                        editableStart = position;
                    editableEnd = position + 1;
                }

                position++;
            }

            if (editableStart < 0)
            {
                editableStart = start;
                editableEnd = end;
            }
        }

        private static byte[] Combine(byte[] first, byte[] second)
        {
            int firstLength = first?.Length ?? 0;
            int secondLength = second?.Length ?? 0;
            byte[] result = new byte[firstLength + secondLength];
            if (firstLength > 0)
                Buffer.BlockCopy(first, 0, result, 0, firstLength);
            if (secondLength > 0)
                Buffer.BlockCopy(second, 0, result, firstLength, secondLength);
            return result;
        }

        private void AddInvalidEntry(int offset, string text)
        {
            Strings.Add(new EVEString(nextStringIndex++, offset, text, false));
        }

        private static string FormatExportText(string text, bool removeSplit)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            int first = -1;
            int last = -1;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == '{')
                {
                    int close = text.IndexOf('}', index + 1);
                    if (close >= 0)
                    {
                        index = close;
                        continue;
                    }
                }

                if (text[index] == '\\' && index + 1 < text.Length
                    && (text[index + 1] == 'n' || text[index + 1] == 'r'))
                {
                    if (first < 0)
                        first = index;
                    last = index + 2;
                    index++;
                    continue;
                }

                if (IsTextCharacter(text[index]))
                {
                    if (first < 0)
                        first = index;
                    last = index + 1;
                }
            }

            if (first < 0)
                return string.Empty;

            string value = text.Substring(first, last - first);
            return removeSplit ? value.Replace("{FF 01}", " ") : value;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
            => (ushort)((data[offset] << 8) | data[offset + 1]);

        private static int GetLogicalTextEnd(byte[] data, int textStart)
        {
            for (int i = data.Length - 1; i >= textStart; i--)
                if (data[i] != 0)
                    return i + 1;
            return textStart;
        }

        private static byte[] CopyBytes(byte[] data, int offset, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(data, offset, result, 0, length);
            return result;
        }

        internal static string Escape(byte[] data, int offset, int length)
        {
            var builder = new StringBuilder(length);
            int end = offset + length;
            for (int i = offset; i < end; i++)
            {
                if (data[i] == 0xFF)
                {
                    int commandLength = GetCommandLength(data, i, end);
                    AppendByteGroup(builder, data, i, commandLength);
                    i += commandLength;

                    // FF 05 introduces a two-byte order value, which is raw
                    // data rather than part of the FF 05 opcode itself.
                    if (commandLength == 2 && data[i - commandLength + 1] == 0x05)
                    {
                        int orderLength = Math.Min(2, end - i);
                        if (orderLength > 0)
                        {
                            AppendByteGroup(builder, data, i, orderLength);
                            i += orderLength;
                        }
                    }
                    i--;
                }
                else if (data[i] == 0x0F)
                {
                    int orderLength = Math.Min(2, end - i);
                    AppendByteGroup(builder, data, i, orderLength);
                    i += orderLength - 1;
                }
                else if (data[i] >= 0x20 && data[i] <= 0x7E && data[i] != '{' && data[i] != '}')
                    builder.Append((char)data[i]);
                else if (data[i] == 0x0A)
                    builder.Append("\\n");
                else if (data[i] == 0x0D)
                    builder.Append("\\r");
                else
                {
                    int rawStart = i;
                    while (i < end && !IsTextByte(data[i])
                        && data[i] != 0x0A && data[i] != 0x0D
                        && data[i] != 0x0F && data[i] != 0xFF)
                        i++;
                    AppendByteGroup(builder, data, rawStart, i - rawStart);
                    i--;
                }
            }
            return builder.ToString();
        }

        internal static byte[] Unescape(string text)
        {
            var result = new List<byte>(text?.Length ?? 0);
            text ??= string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close >= 0 && TryParseHexBytes(text.Substring(i + 1, close - i - 1), out byte[] command))
                    {
                        result.AddRange(command);
                        i = close;
                        continue;
                    }
                }

                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    if (text[i + 1] == 'n')
                    {
                        result.Add(0x0A);
                        i++;
                        continue;
                    }
                    if (text[i + 1] == 'r')
                    {
                        result.Add(0x0D);
                        i++;
                        continue;
                    }
                }

                if (text[i] == '<' && i + 3 < text.Length && text[i + 3] == '>')
                {
                    int high = HexValue(text[i + 1]);
                    int low = HexValue(text[i + 2]);
                    if (high >= 0 && low >= 0)
                    {
                        result.Add((byte)((high << 4) | low));
                        i += 3;
                        continue;
                    }
                }

                if (text[i] > 0x7F)
                    throw new InvalidDataException("EVE text must use ASCII or {XX ...} byte escapes.");
                result.Add((byte)text[i]);
            }
            return result.ToArray();
        }

        private static int GetCommandLength(byte[] data, int offset, int end)
        {
            if (offset + 1 >= end)
                return 1;

            switch (data[offset + 1])
            {
                case 0x19 when offset + 3 < end && data[offset + 2] == 0xC7 && data[offset + 3] == 0xB7:
                    return 4;
                default:
                    return 2;
            }
        }

        private static bool IsTextByte(byte value)
            => value >= 0x20 && value <= 0x7E && value != '{' && value != '}';

        private static bool IsTextCharacter(char value)
            => value >= 0x20 && value <= 0x7E && value != '{' && value != '}';

        private static void AppendByteGroup(StringBuilder builder, byte[] data, int offset, int length)
        {
            builder.Append('{');
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                    builder.Append(' ');
                builder.Append(data[offset + i].ToString("X2"));
            }
            builder.Append('}');
        }

        private static bool TryParseHexBytes(string value, out byte[] result)
        {
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                result = null;
                return false;
            }

            result = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length != 2 || !byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out result[i]))
                {
                    result = null;
                    return false;
                }
            }
            return true;
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'A' && value <= 'F')
                return value - 'A' + 10;
            if (value >= 'a' && value <= 'f')
                return value - 'a' + 10;
            return -1;
        }

        public object Tag { get; set; }

        private sealed class EVEChunk
        {
            public int Offset { get; }
            public List<EVEString> Entries { get; } = new List<EVEString>();
            public List<byte[]> Separators { get; } = new List<byte[]>();
            public bool HasChanges => Entries.Any(x => x.Text != x.OriginalText);

            public EVEChunk(int offset)
            {
                Offset = offset;
            }

            public byte[] GetData()
            {
                var result = new List<byte>();
                for (int i = 0; i < Entries.Count; i++)
                {
                    result.AddRange(Entries[i].GetData());
                    if (i < Separators.Count)
                        result.AddRange(Separators[i]);
                }
                return result.ToArray();
            }
        }
    }

    public enum EVEStringKind
    {
        Text,
        Speaker
    }

    public sealed class EVEString : INotifyPropertyChanged
    {
        private string text;
        private readonly byte[] prefix;
        private readonly byte[] suffix;

        public int Index { get; }
        public int Offset { get; }
        public bool IsEditable { get; }
        public EVEStringKind Kind { get; }
        public string OriginalText { get; }
        public EVEString Speaker { get; internal set; }

        public string Text
        {
            get { return text; }
            set
            {
                if (text == value)
                    return;
                text = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        internal EVEString(int index, int offset, string text, bool isEditable,
            EVEStringKind kind = EVEStringKind.Text, byte[] prefix = null, byte[] suffix = null)
        {
            Index = index;
            Offset = offset;
            OriginalText = text;
            this.text = text;
            IsEditable = isEditable;
            Kind = kind;
            this.prefix = prefix?.ToArray() ?? Array.Empty<byte>();
            this.suffix = suffix?.ToArray() ?? Array.Empty<byte>();
        }

        internal byte[] GetData()
        {
            byte[] textData = EVE.Unescape(Text);
            byte[] result = new byte[prefix.Length + textData.Length + suffix.Length];
            if (prefix.Length > 0)
                Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
            if (textData.Length > 0)
                Buffer.BlockCopy(textData, 0, result, prefix.Length, textData.Length);
            if (suffix.Length > 0)
                Buffer.BlockCopy(suffix, 0, result, prefix.Length + textData.Length, suffix.Length);
            return result;
        }

        internal void Reset()
        {
            Text = OriginalText;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
