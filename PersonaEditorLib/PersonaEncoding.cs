using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditorLib
{
    public class PersonaEncoding : Encoding
    {
        public static PersonaEncoding Empty { get; } = new PersonaEncoding();

        public string Tag { get; set; } = "Empty";

        public string FilePath { get; } = "";

        public Dictionary<int, char> Dictionary { get; } = new Dictionary<int, char>();
        public Dictionary<int, byte[]> CustomBytes { get; } = new Dictionary<int, byte[]>();

        private readonly Dictionary<char, int> indexByChar = new Dictionary<char, int>();
        private readonly Dictionary<string, int> indexByCustomBytes = new Dictionary<string, int>();
        private int longestCustomByteSequence;
        private bool lookupsDirty;

        public PersonaEncoding()
        {
        }

        public PersonaEncoding(string fontMap)
        {
            if (File.Exists(fontMap))
            {
                if (Path.GetExtension(fontMap).Equals(".FNTMAP2", StringComparison.OrdinalIgnoreCase))
                    OpenFNTMAP2(fontMap);
                else
                    OpenFNTMAP(fontMap);
            }

            Tag = Path.GetFileNameWithoutExtension(fontMap);
            FilePath = Path.GetFullPath(fontMap);
        }

        public void Add(int index, char c)
            => Add(index, c, null);

        public void Add(int index, char c, byte[] customBytes)
        {
            if (c == '\0')
                return;

            Dictionary[index] = c;
            if (customBytes?.Length > 0)
                CustomBytes[index] = customBytes.ToArray();
            else
                CustomBytes.Remove(index);

            lookupsDirty = true;
        }

        #region FNTMAP

        public void SaveFNTMAP2(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (StreamWriter writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(false)))
                {
                    int maxIndex = Dictionary.Count == 0 ? -1 : Dictionary.Keys.Max();
                    for (int index = 0; index <= maxIndex; index++)
                    {
                        char value = Dictionary.TryGetValue(index, out char mappedChar) ? mappedChar : '\0';
                        writer.Write(EscapeFNTMAP2Char(value));

                        if (CustomBytes.TryGetValue(index, out byte[] customBytes) && customBytes.Length > 0)
                        {
                            writer.Write('\t');
                            writer.Write(string.Join(" ", customBytes.Select(x => x.ToString("X2"))));
                        }

                        writer.WriteLine();
                    }
                }

                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private void OpenFNTMAP(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int count = checked((int)(stream.Length / 2));
                byte[] buffer = new byte[2];

                for (int index = 0; index < count; index++)
                {
                    if (stream.Read(buffer, 0, buffer.Length) != buffer.Length)
                        break;

                    Add(index, Unicode.GetChars(buffer)[0]);
                }
            }
        }

        private void OpenFNTMAP2(string path)
        {
            int index = 0;
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                string[] columns = line.Split('\t');
                char value = UnescapeFNTMAP2Char(columns[0]);
                byte[] customBytes = columns.Length == 2 && TryParseCustomBytes(columns[1], out byte[] parsed)
                    ? parsed
                    : null;

                Add(index, value, customBytes);
                index++;
            }
        }

        private static string EscapeFNTMAP2Char(char value)
        {
            if (value == '\0' || char.IsControl(value) || value == ' ')
                return $"\\u{(int)value:X4}";
            if (value == '\\')
                return "\\\\";

            return value.ToString();
        }

        private static char UnescapeFNTMAP2Char(string value)
        {
            if (string.IsNullOrEmpty(value))
                return '\0';
            if (value == "\\\\")
                return '\\';
            if (value.Length == 6 && value.StartsWith("\\u", StringComparison.OrdinalIgnoreCase)
                && ushort.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out ushort code))
                return (char)code;

            return value[0];
        }

        private static bool TryParseCustomBytes(string value, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string compact = string.Concat(value.Where(x => !char.IsWhiteSpace(x)));
            if (compact.Length == 0 || compact.Length % 2 != 0)
                return false;

            byte[] parsed = new byte[compact.Length / 2];
            for (int i = 0; i < parsed.Length; i++)
            {
                if (!byte.TryParse(compact.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out parsed[i]))
                    return false;
            }

            bytes = parsed;
            return true;
        }

        #endregion FNTMAP

        public char GetChar(int index)
            => Dictionary.TryGetValue(index, out char value) ? value : '\uFFFD';

        public int GetIndex(char c)
        {
            EnsureLookups();
            return indexByChar.TryGetValue(c, out int index) ? index : -1;
        }

        public bool TryGetCustomBytes(int index, out byte[] bytes)
        {
            if (CustomBytes.TryGetValue(index, out byte[] customBytes))
            {
                bytes = customBytes.ToArray();
                return true;
            }

            bytes = null;
            return false;
        }

        public bool TryGetGlyphIndex(byte[] bytes, int offset, int count, out int glyphIndex, out int byteCount)
        {
            glyphIndex = -1;
            byteCount = 0;
            if (bytes == null || offset < 0 || count < 0 || offset > bytes.Length - count || count == 0)
                return false;

            EnsureLookups();

            int customLength = Math.Min(longestCustomByteSequence, count);
            for (int length = customLength; length > 0; length--)
            {
                if (indexByCustomBytes.TryGetValue(GetByteKey(bytes, offset, length), out glyphIndex))
                {
                    byteCount = length;
                    return true;
                }
            }

            byte first = bytes[offset];
            if (first >= 0x20 && first < 0x80)
            {
                glyphIndex = first;
                byteCount = 1;
                return true;
            }

            if (first >= 0x80 && first < 0xF0 && count >= 2)
            {
                glyphIndex = (first - 0x81) * 0x80 + bytes[offset + 1] + 0x20;
                byteCount = 2;
                return true;
            }

            return false;
        }

        private static string GetByteKey(byte[] bytes, int offset, int count)
            => System.Convert.ToHexString(bytes, offset, count);

        private void RebuildLookups()
        {
            indexByChar.Clear();
            indexByCustomBytes.Clear();
            longestCustomByteSequence = 0;

            foreach (var item in Dictionary)
            {
                if (!indexByChar.ContainsKey(item.Value))
                    indexByChar.Add(item.Value, item.Key);

                if (CustomBytes.TryGetValue(item.Key, out byte[] customBytes) && customBytes.Length > 0)
                {
                    string key = GetByteKey(customBytes, 0, customBytes.Length);
                    if (!indexByCustomBytes.ContainsKey(key))
                        indexByCustomBytes.Add(key, item.Key);
                    longestCustomByteSequence = Math.Max(longestCustomByteSequence, customBytes.Length);
                }
            }

            lookupsDirty = false;
        }

        private void EnsureLookups()
        {
            if (lookupsDirty)
                RebuildLookups();
        }

        private byte[] GetBytesForChar(char value)
        {
            int index = GetIndex(value);
            if (index < 0)
                return Array.Empty<byte>();
            if (CustomBytes.TryGetValue(index, out byte[] customBytes))
                return customBytes;

            return GetDefaultBytes(index);
        }

        private static byte[] GetDefaultBytes(int index)
        {
            if (index >= 0 && index < 0x80)
                return new[] { (byte)index };
            if (index < 0x80)
                return Array.Empty<byte>();

            int byte2 = ((index - 0x20) % 0x80) + 0x80;
            int byte1 = ((index - 0x20 - byte2) / 0x80) + 0x81;
            if (byte1 < byte.MinValue || byte1 > byte.MaxValue)
                return Array.Empty<byte>();

            return new[] { (byte)byte1, (byte)byte2 };
        }

        #region Encoding

        public override int GetByteCount(char[] chars, int index, int count)
        {
            int byteCount = 0;
            for (int i = index; i < index + count; i++)
                byteCount += GetBytesForChar(chars[i]).Length;

            return byteCount;
        }

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            int written = 0;
            for (int i = charIndex; i < charIndex + charCount; i++)
            {
                byte[] encoded = GetBytesForChar(chars[i]);
                Buffer.BlockCopy(encoded, 0, bytes, byteIndex + written, encoded.Length);
                written += encoded.Length;
            }

            return written;
        }

        public override int GetCharCount(byte[] bytes, int index, int count)
        {
            int charCount = 0;
            int position = index;
            int end = index + count;
            while (position < end)
            {
                if (TryGetGlyphIndex(bytes, position, end - position, out _, out int consumed))
                    position += consumed;
                else
                    position++;

                charCount++;
            }

            return charCount;
        }

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            int written = 0;
            int position = byteIndex;
            int end = byteIndex + byteCount;
            while (position < end)
            {
                if (TryGetGlyphIndex(bytes, position, end - position, out int glyphIndex, out int consumed))
                {
                    chars[charIndex + written] = GetChar(glyphIndex);
                    position += consumed;
                }
                else
                {
                    chars[charIndex + written] = '\uFFFD';
                    position++;
                }

                written++;
            }

            return written;
        }

        public override int GetMaxByteCount(int charCount)
        {
            EnsureLookups();
            return charCount * Math.Max(2, longestCustomByteSequence);
        }

        public override int GetMaxCharCount(int byteCount)
            => byteCount;

        #endregion Encoding
    }
}
