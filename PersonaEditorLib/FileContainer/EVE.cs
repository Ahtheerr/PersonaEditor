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
        private const string UppercaseAccents = "ÀÁÃÂÇÉÊÍÓÕÔÚ";
        private const string LowercaseAccents = "àáãâçéêíóõôú";
        private const byte AccentPrefix = 0xE6;
        private const byte UppercaseAccentStart = 0xA0;
        private const byte LowercaseAccentStart = 0xAC;
        private readonly ushort[] pointers;
        private readonly bool hasPointerPadding;
        private readonly byte[] originalData;
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
            if (!TryParseLayout(data, 0, out ushort formatVersion, out ushort codeTableEnd,
                out ushort stringTableStart, out ushort textStart, out ushort[] parsedPointers,
                out bool pointerPadding, out int rawSize, out string error))
                throw new InvalidDataException($"EVE: {error}");

            Data = CopyBytes(data, 0, rawSize);
            originalData = (byte[])Data.Clone();
            FormatVersion = formatVersion;
            CodeTableEnd = codeTableEnd;
            StringTableStart = stringTableStart;
            TextStart = textStart;
            pointers = parsedPointers;
            hasPointerPadding = pointerPadding;

            for (int i = 0; i < pointers.Length; i++)
            {
                int start = pointers[i];
                int end = i + 1 < pointers.Length ? pointers[i + 1] : rawSize - TextStart;
                EVEChunk chunk = new EVEChunk(start);
                AddChunkEntries(chunk, start, end);
                chunks.Add(chunk);
                foreach (EVEString entry in chunk.Entries)
                    Strings.Add(entry);
            }
        }

        public static bool IsEve(byte[] data)
            => TryParseLayout(data, 0, out _, out _, out _, out _, out _, out _, out _, out _);

        internal static bool TryGetRawSize(byte[] data, int offset, out int rawSize)
        {
            bool valid = TryParseLayout(data, offset, out _, out _, out _, out _, out _, out _,
                out rawSize, out _);
            return valid;
        }

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

        private static bool TryParseLayout(byte[] data, int offset, out ushort formatVersion,
            out ushort codeTableEnd, out ushort stringTableStart, out ushort textStart,
            out ushort[] recordPointers, out bool pointerPadding, out int rawSize, out string error)
        {
            recordPointers = Array.Empty<ushort>();
            pointerPadding = false;
            rawSize = 0;
            error = "invalid event header.";
            if (!TryReadHeader(data, offset, out formatVersion, out codeTableEnd,
                out stringTableStart, out textStart))
                return false;

            int tableSize = textStart - stringTableStart;
            if ((tableSize & 1) != 0)
            {
                error = "text-record offset table has an odd size.";
                return false;
            }

            int wordCount = tableSize / 2;
            ushort[] words = new ushort[wordCount];
            for (int i = 0; i < words.Length; i++)
                words[i] = ReadUInt16(data, offset + stringTableStart + i * 2);

            pointerPadding = words.Length > 1 && words[words.Length - 1] == 0;
            int pointerCount = words.Length - (pointerPadding ? 1 : 0);
            recordPointers = new ushort[pointerCount];
            Array.Copy(words, recordPointers, pointerCount);

            if (recordPointers.Length == 0)
            {
                rawSize = stringTableStart;
                error = null;
                return true;
            }
            if (recordPointers[0] != 0)
            {
                error = "text-record offset table does not start at zero.";
                return false;
            }
            for (int i = 1; i < recordPointers.Length; i++)
                if (recordPointers[i - 1] >= recordPointers[i])
                {
                    error = "text-record offsets are not strictly increasing.";
                    return false;
                }

            int blobStart = offset + textStart;
            int lastStart = blobStart + recordPointers[recordPointers.Length - 1];
            if (lastStart >= data.Length)
            {
                error = "last text-record offset is outside the file.";
                return false;
            }

            int lastEnd = FindSequence(data, lastStart, data.Length, 0xFF, 0x00);
            if (lastEnd < 0)
            {
                error = "last text record has no FF 00 terminator.";
                return false;
            }
            rawSize = lastEnd + 2 - offset;

            for (int i = 0; i < recordPointers.Length; i++)
            {
                int start = blobStart + recordPointers[i];
                int end = i + 1 < recordPointers.Length
                    ? blobStart + recordPointers[i + 1]
                    : offset + rawSize;
                if (end > data.Length || end - start < 2
                    || data[end - 2] != 0xFF || data[end - 1] != 0x00)
                {
                    error = $"text record {i} does not end in FF 00.";
                    return false;
                }
                if (FindSequence(data, start, end - 2, 0xFF, 0x00) >= 0)
                {
                    error = $"text record {i} contains an early FF 00 terminator.";
                    return false;
                }
            }

            error = null;
            return true;
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

        public bool MatchesExportedText(int index, string sourceText, bool removeSplit)
        {
            EVEString entry = Strings.FirstOrDefault(x => x.Index == index
                && x.IsEditable && x.Kind == EVEStringKind.Text);
            return entry != null && string.Equals(
                FormatExportText(entry.OriginalText, removeSplit), sourceText ?? string.Empty,
                StringComparison.Ordinal);
        }

        public FormatEnum Type => FormatEnum.EVE;

        public List<GameFile> GetSubFiles() => SubFiles;

        public int GetSize() => GetData().Length;

        public byte[] GetData()
        {
            if (!HasChanges)
                return Data;

            var output = new List<byte>(originalData.Length);
            for (int i = 0; i < StringTableStart; i++)
                output.Add(originalData[i]);

            int cursor = 0;
            var records = new List<byte[]>(chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                if (cursor > ushort.MaxValue)
                    throw new InvalidDataException($"EVE: text record {i} offset exceeds 16 bits.");
                output.Add((byte)(cursor >> 8));
                output.Add((byte)cursor);

                byte[] record = chunks[i].HasChanges
                    ? chunks[i].GetData()
                    : CopyBytes(originalData, TextStart + pointers[i],
                        (i + 1 < pointers.Length ? pointers[i + 1] : originalData.Length - TextStart) - pointers[i]);
                ValidateRecord(record, i);
                records.Add(record);
                cursor = checked(cursor + record.Length);
            }

            if (hasPointerPadding)
            {
                output.Add(0);
                output.Add(0);
            }
            if (output.Count != TextStart)
                throw new InvalidDataException("EVE: rebuilt text-record offset table changed size.");

            foreach (byte[] record in records)
                output.AddRange(record);

            return output.ToArray();
        }

        private static void ValidateRecord(byte[] record, int index)
        {
            if (record.Length < 2 || record[record.Length - 2] != 0xFF || record[record.Length - 1] != 0x00)
                throw new InvalidDataException($"EVE: text record {index} must end in FF 00.");
            if (FindSequence(record, 0, record.Length - 2, 0xFF, 0x00) >= 0)
                throw new InvalidDataException($"EVE: text record {index} contains an early FF 00 terminator.");
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
                chunk.AddSeparator(TextboxSeparator);
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
                if (TryDecodeAccent(originalData, TextStart + prefixStart, TextStart + end, out _))
                    return false;

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
                if (TryDecodeAccent(originalData, TextStart + position, TextStart + end, out _))
                {
                    if (editableStart < 0)
                        editableStart = position;
                    editableEnd = position + 2;
                    position += 2;
                    continue;
                }

                byte value = originalData[TextStart + position];
                if (value == 0xFF)
                {
                    int commandLength = GetCommandLength(originalData, TextStart + position, TextStart + end);
                    if (IsLeadingTextControl(originalData, TextStart + position, TextStart + end))
                    {
                        if (editableStart < 0)
                            editableStart = position;
                        editableEnd = position + commandLength;
                    }
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

            bool hasText = false;
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
                    hasText = true;
                    index++;
                    continue;
                }

                if (IsTextCharacter(text[index]))
                    hasText = true;
            }

            if (!hasText)
                return string.Empty;

            string value = text;
            return removeSplit ? value.Replace("{FF 01}", " ") : value;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
            => (ushort)((data[offset] << 8) | data[offset + 1]);

        private static int FindSequence(byte[] data, int start, int end, byte first, byte second)
        {
            int limit = Math.Min(end, data.Length);
            for (int i = Math.Max(0, start); i + 1 < limit; i++)
                if (data[i] == first && data[i + 1] == second)
                    return i;
            return -1;
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
                if (TryDecodeAccent(data, i, end, out char accent))
                {
                    builder.Append(accent);
                    i++;
                }
                else if (data[i] == 0xFF)
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
                    while (i < end && !TryDecodeAccent(data, i, end, out _)
                        && !IsTextByte(data[i])
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
                if (TryEncodeAccent(text[i], out byte accentByte))
                {
                    result.Add(AccentPrefix);
                    result.Add(accentByte);
                    continue;
                }

                if (text[i] == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close >= 0 && TryParseHexBytes(text.Substring(i + 1, close - i - 1), out byte[] command))
                    {
                        result.AddRange(command);
                        i = close;
                        continue;
                    }

                    throw new InvalidDataException(
                        $"Invalid byte escape at character {i}. Use {{XX XX ...}} with two hexadecimal digits per byte.");
                }

                if (text[i] == '}')
                    throw new InvalidDataException(
                        $"Unexpected '}}' at character {i}. Encode a literal closing brace as {{7D}}.");

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
                    throw new InvalidDataException(
                        $"Character '{text[i]}' (U+{(int)text[i]:X4}) has no EVE byte mapping. " +
                        "Use a supported character or a {XX ...} byte escape.");
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
            => (value >= 0x20 && value <= 0x7E && value != '{' && value != '}')
                || UppercaseAccents.IndexOf(value) >= 0
                || LowercaseAccents.IndexOf(value) >= 0;

        // FF 82 is an inline placeholder and can become the first text byte
        // when a translation removes the source string's leading space.
        private static bool IsLeadingTextControl(byte[] data, int offset, int end)
            => offset + 1 < end && data[offset] == 0xFF && data[offset + 1] == 0x82;

        private static bool TryDecodeAccent(byte[] data, int offset, int end, out char value)
        {
            value = default;
            if (offset < 0 || offset + 1 >= end || data[offset] != AccentPrefix)
                return false;

            int index = data[offset + 1] - UppercaseAccentStart;
            if (index >= 0 && index < UppercaseAccents.Length)
            {
                value = UppercaseAccents[index];
                return true;
            }

            index = data[offset + 1] - LowercaseAccentStart;
            if (index >= 0 && index < LowercaseAccents.Length)
            {
                value = LowercaseAccents[index];
                return true;
            }

            return false;
        }

        private static bool TryEncodeAccent(char value, out byte encoded)
        {
            int index = UppercaseAccents.IndexOf(value);
            if (index >= 0)
            {
                encoded = (byte)(UppercaseAccentStart + index);
                return true;
            }

            index = LowercaseAccents.IndexOf(value);
            if (index >= 0)
            {
                encoded = (byte)(LowercaseAccentStart + index);
                return true;
            }

            encoded = 0;
            return false;
        }

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
            public bool HasChanges => Entries.Any(x => x.Text != x.OriginalText);
            private readonly List<byte[]> separators = new List<byte[]>();
            private readonly List<int> separatorEntryIndices = new List<int>();

            public EVEChunk(int offset)
            {
                Offset = offset;
            }

            public void AddSeparator(byte[] separator)
            {
                separators.Add(separator);
                separatorEntryIndices.Add(Entries.Count - 1);
            }

            public byte[] GetData()
            {
                var result = new List<byte>();
                int separatorIndex = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    result.AddRange(Entries[i].GetData());
                    while (separatorIndex < separators.Count
                        && separatorEntryIndices[separatorIndex] == i)
                    {
                        result.AddRange(separators[separatorIndex]);
                        separatorIndex++;
                    }
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
            byte[] textData;
            try
            {
                textData = EVE.Unescape(Text);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException(
                    $"EVE entry {Index} ({Kind}): {e.Message}", e);
            }

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
