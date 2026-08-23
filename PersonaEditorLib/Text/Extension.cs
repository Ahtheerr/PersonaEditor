using AuxiliaryLibraries.Extensions;
using AuxiliaryLibraries.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonaEditorLib.Text
{
    public static class Extension
    {
        private const string RawNoAutoPrefix = "[RN]";

        public static string NormalizeImportedText(this string text)
        {
            text ??= string.Empty;
            if (text.StartsWith(RawNoAutoPrefix, StringComparison.OrdinalIgnoreCase))
                text = text.Substring(RawNoAutoPrefix.Length);
            return text.Replace("\\n", "\n");
        }

        public static string SplitByWidthOrImportedRaw(this string text, Dictionary<char, int> charWidth, int width)
            => IsImportedRaw(text) ? text.NormalizeImportedText() : text.SplitByWidth(charWidth, width);

        public static string SplitByLineCountOrImportedRaw(this string text, Dictionary<char, int> charWidth, int lineCount)
            => IsImportedRaw(text) ? text.NormalizeImportedText() : text.SplitByLineCount(charWidth, lineCount);

        private static bool IsImportedRaw(string text)
            => text != null && text.StartsWith(RawNoAutoPrefix, StringComparison.OrdinalIgnoreCase);

        public static IEnumerable<TextBaseElement> GetTextBases(this string s, Encoding enc)
        {
            if (s == null)
                throw new ArgumentNullException(nameof(s));
            if (enc == null)
                throw new ArgumentNullException(nameof(enc));

            foreach (var a in Regex.Split(s, "(\r\n|\r|\n)"))
                if (Regex.IsMatch(a, "\r\n|\r|\n"))
                    yield return new TextBaseElement(false, new byte[] { 0x0A });
                else
                    foreach (var b in Regex.Split(a, @"({[^}]+})"))
                        if (Regex.IsMatch(b, @"{.+}") && StringTool.TryParseArray(b.Substring(1, b.Length - 2), out byte[] parsed))
                            yield return new TextBaseElement(false, parsed);
                        else
                            yield return new TextBaseElement(true, enc.GetBytes(b));
        }

        public static IEnumerable<TextBaseElement> GetTextBases(this byte[] array)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            List<byte> temp = new List<byte>();

            for (int i = 0; i < array.Length; i++)
            {
                if (0x20 <= array[i] && array[i] < 0x80)
                {
                    temp.Add(array[i]);
                }
                else if (0x80 <= array[i] && array[i] < 0xF0)
                {
                    if (i + 1 >= array.Length)
                    {
                        yield return new TextBaseElement(false, array.Skip(i).ToArray());
                        yield break;
                    }

                    temp.Add(array[i]);
                    i++;
                    temp.Add(array[i]);
                }
                else
                {
                    if (0x00 <= array[i] && array[i] < 0x20)
                    {
                        if (temp.Count != 0)
                        {
                            yield return new TextBaseElement(true, temp.ToArray());
                            temp.Clear();
                        }

                        temp.Add(array[i]);
                        yield return new TextBaseElement(false, temp.ToArray());
                        temp.Clear();
                    }
                    else
                    {
                        if (temp.Count != 0)
                        {
                            yield return new TextBaseElement(true, temp.ToArray());
                            temp.Clear();
                        }

                        temp.Add(array[i]);
                        int count = (array[i] - 0xF0) * 2 - 1;
                        if (count > array.Length - i - 1)
                        {
                            yield return new TextBaseElement(false, array.Skip(i).ToArray());
                            yield break;
                        }

                        for (int k = 0; k < count; k++)
                        {
                            i++;
                            temp.Add(array[i]);
                        }

                        yield return new TextBaseElement(false, temp.ToArray());
                        temp.Clear();
                    }
                }
            }

            if (temp.Count != 0)
            {
                yield return new TextBaseElement(true, temp.ToArray());
                temp.Clear();
            }
        }

        public static IEnumerable<TextBaseElement> GetTextBases(this byte[] array, global::PersonaEditorLib.PersonaEncoding encoding)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (encoding == null)
                throw new ArgumentNullException(nameof(encoding));

            List<byte> textBytes = new List<byte>();
            for (int position = 0; position < array.Length;)
            {
                if (encoding.TryGetGlyphIndex(array, position, array.Length - position, out _, out int byteCount))
                {
                    textBytes.AddRange(array.Skip(position).Take(byteCount));
                    position += byteCount;
                    continue;
                }

                if (textBytes.Count > 0)
                {
                    yield return new TextBaseElement(true, textBytes.ToArray());
                    textBytes.Clear();
                }

                byte value = array[position++];
                if (value < 0x20)
                {
                    yield return new TextBaseElement(false, new[] { value });
                    continue;
                }

                if (value < 0xF0)
                {
                    if (position >= array.Length)
                    {
                        yield return new TextBaseElement(false, array.Skip(position - 1).ToArray());
                        yield break;
                    }

                    textBytes.Add(value);
                    textBytes.Add(array[position++]);
                    continue;
                }

                int payloadLength = Math.Max(0, (value - 0xF0) * 2 - 1);
                if (payloadLength > array.Length - position)
                {
                    yield return new TextBaseElement(false, array.Skip(position - 1).ToArray());
                    yield break;
                }

                byte[] control = new byte[payloadLength + 1];
                control[0] = value;
                if (payloadLength > 0)
                {
                    Buffer.BlockCopy(array, position, control, 1, payloadLength);
                    position += payloadLength;
                }

                yield return new TextBaseElement(false, control);
            }

            if (textBytes.Count > 0)
                yield return new TextBaseElement(true, textBytes.ToArray());
        }

        /// <summary>
        /// Splits Persona 3 Reload message bytes into UTF-8 text and raw control codes.
        /// Reload prefixes each message function with FE; the low nibble of the next
        /// byte determines the number of encoded ushort arguments.
        /// </summary>
        public static IEnumerable<TextBaseElement> GetReloadTextBases(this byte[] array)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            int position = 0;
            while (position < array.Length)
            {
                byte value = array[position];
                if (value == 0 || value == 0x0A)
                {
                    yield return new TextBaseElement(false, new[] { value });
                    position++;
                    continue;
                }

                if (value == 0xFE)
                {
                    int controlLength = GetReloadControlLength(array, position);
                    yield return new TextBaseElement(false, array.Skip(position).Take(controlLength).ToArray());
                    position += controlLength;
                    continue;
                }

                int start = position++;
                while (position < array.Length
                    && array[position] != 0
                    && array[position] != 0x0A
                    && array[position] != 0xFE)
                {
                    position++;
                }

                yield return new TextBaseElement(true, array.Skip(start).Take(position - start).ToArray());
            }
        }

        private static int GetReloadControlLength(byte[] array, int position)
        {
            if (array.Length - position < 3)
                return array.Length - position;

            int argumentPairs = (array[position + 1] & 0x0F) - 1;
            if (argumentPairs < 0)
                return 1;

            int length = 3 + argumentPairs * 2;
            return Math.Min(length, array.Length - position);
        }

        public static List<string> SplitBySystem(this string str)
        {
            List<string> returned = new List<string>();

            foreach (var a in Regex.Split(str, "(\r\n|\r|\n)"))
                if (Regex.IsMatch(a, "\r\n|\r|\n"))
                    returned.Add("{0A}");
                else
                    foreach (var b in Regex.Split(a, @"({[^}]+})"))
                        returned.Add(b);

            return returned;
        }
        
        public static string SplitByWidth(this string String, Dictionary<char, int> charWidth, int width)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            var temp = GetStringWidth(String, charWidth);
            List<string> tempStr = temp.Item1;
            List<int> tempWidth = temp.Item2;

            List<string> result = new List<string>();
            string input = "";
            int widthsum = 0;
            for (int i = 0; i < tempStr.Count; i++)
            {
                if (widthsum == 0)
                {
                    if (tempStr[i] != " ")
                    {
                        widthsum += tempWidth[i];
                        input += tempStr[i];
                    }
                    else if (i + 1 < tempStr.Count && tempStr[i + 1].Equals("{0A}", StringComparison.CurrentCultureIgnoreCase))
                    {
                        widthsum += tempWidth[i];
                        input += tempStr[i];
                    }
                }
                else
                {
                    if (widthsum + tempWidth[i] > width)
                    {
                        result.Add(input);
                        i--;
                        widthsum = 0;
                        input = "";
                    }
                    else
                    {
                        widthsum += tempWidth[i];
                        input += tempStr[i];
                    }
                }
            }
            if (input != "")
                result.Add(input);

            return string.Join("\n", result);
        }

        public static string SplitByLineCount(this string String, Dictionary<char, int> charWidth, int lineCount)
        {
            if (lineCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(lineCount));

            var temp = GetStringWidth(String, charWidth);

            List<string> tempStr = temp.Item1;
            List<int> tempWidth = temp.Item2;

            List<int> indexies = new List<int>();
            List<string> returned = new List<string>();
            int width = tempWidth.Sum() / lineCount;
            int tempwidth = 0;
            int tempind = 0;
            for (int i = 0; i < tempWidth.Count; i++)
            {
                if (tempwidth + tempWidth[i] > width)
                {
                    indexies.Add(tempind);
                    tempind = i + 1;
                    tempwidth = 0;
                    if (indexies.Count == lineCount)
                        break;
                }
                else
                    tempwidth += tempWidth[i];
            }
            if (indexies.Count != lineCount)
                indexies.Add(tempind);

            var splitedByLineCount = String.Join("\n", tempStr.ToArray().Split(indexies.ToArray()).Select(x => String.Join("", x)).Select(x => x.TrimStart(' ')));

            return splitedByLineCount;
        }

        private static (List<string>, List<int>) GetStringWidth(string str, Dictionary<char, int> charWidth)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            if (charWidth == null)
                throw new ArgumentNullException(nameof(charWidth));

            string input = String.Join(" ", Regex.Split(str, @"\\n|\r\n|\r|\n"));

            List<string> tempStr = new List<string>();
            List<bool> tempBool = new List<bool>();
            List<int> tempWidth = new List<int>();

            var split = input.SplitBySystem();
            foreach (var a in split)
            {
                bool isControlCode = a.Length >= 2
                    && a[0] == '{'
                    && a[a.Length - 1] == '}'
                    && StringTool.TryParseArray(a.Substring(1, a.Length - 2), out _);

                if (isControlCode)
                {
                    tempStr.Add(a);
                    tempBool.Add(false);
                }
                else
                {
                    foreach (var b in Regex.Split(a, @"( )").Where(x => x != ""))
                    {
                        tempStr.Add(b);
                        tempBool.Add(true);
                    }
                }
            }

            for (int i = 0; i < tempStr.Count; i++)
            {
                if (tempBool[i])
                {
                    int temp = 0;
                    foreach (var a in tempStr[i])
                        if (charWidth.ContainsKey(a))
                            temp += charWidth[a];

                    tempWidth.Add(temp);
                }
                else
                {
                    if (tempStr[i].Equals("{F1 81}") | tempStr[i].Equals("{F1 82}") | tempStr[i].Equals("{F1 83}"))
                        tempWidth.Add(10);
                    else
                        tempWidth.Add(0);
                }
            }

            return (tempStr, tempWidth);
        }

        public static string MSGListToSystem(this IList<TextBaseElement> list)
        {
            string returned = "";
            foreach (var Bytes in list)
            {
                byte[] temp = Bytes.Data.ToArray();
                if (temp.Length > 0)
                {
                    returned += "{" + System.Convert.ToString(temp[0], 16).PadLeft(2, '0').ToUpper();
                    for (int i = 1; i < temp.Length; i++)
                    {
                        returned += "\u00A0" + System.Convert.ToString(temp[i], 16).PadLeft(2, '0').ToUpper();
                    }
                    returned += "} ";
                }
            }
            return returned;
        }

        public static string GetString(this IEnumerable<TextBaseElement> textBases, Encoding encoding, bool lineSplit = false)
        {
            string returned = "";

            foreach (var MSG in textBases)
                returned += MSG.GetText(encoding, lineSplit);

            return returned;
        }

        public static byte[] GetByteArray(this IEnumerable<TextBaseElement> textBaseElements)
        {
            if (textBaseElements == null)
                throw new ArgumentNullException(nameof(textBaseElements));

            List<byte> temp = new List<byte>();
            foreach (var textBase in textBaseElements)
                temp.AddRange(textBase.Data);
            return temp.ToArray();
        }
    }
}
