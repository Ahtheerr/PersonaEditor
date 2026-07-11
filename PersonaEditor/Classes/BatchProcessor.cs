using AuxiliaryLibraries.WPF.Tools;
using AuxiliaryLibraries.WPF.Wrapper;
using PersonaEditorLib;
using PersonaEditorLib.Other;
using PersonaEditorLib.Text;
using PersonaEditorCMD;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditor.Classes
{
    internal static class BatchProcessor
    {
        public static BatchResult ExportImages(string sourceRoot, string outputRoot)
        {
            var result = new BatchResult();
            foreach (string sourcePath in EnumerateSourceFiles(sourceRoot))
            {
                string fileDir = GetOutputDirectory(sourceRoot, sourcePath, outputRoot);
                var file = OpenSourceFile(sourcePath, result);
                if (file != null)
                    ExportGameFileImages(file, fileDir, result);
            }

            return result;
        }

        public static BatchResult ImportImages(string sourceRoot, string imageRoot)
        {
            var result = new BatchResult();
            Dictionary<string, string> imageFiles = BuildImageIndex(imageRoot);

            foreach (string sourcePath in EnumerateSourceFiles(sourceRoot))
            {
                bool changed = false;
                string sourceRelativeDir = GetRelativeDirectory(sourceRoot, sourcePath);
                var file = OpenSourceFile(sourcePath, result);
                if (file == null)
                    continue;

                ImportGameFileImages(file, imageRoot, sourceRelativeDir, imageFiles, result, ref changed);

                if (changed)
                    SaveFile(sourcePath, file);
            }

            return result;
        }

        public static BatchResult ExportText(string sourceRoot, string outputTextPath)
        {
            var result = new BatchResult();
            var settings = LoadTextSettings();
            string[] sourcePaths = EnumerateSourceFiles(sourceRoot).ToArray();
            Dictionary<string, string> batchTextNames = BuildBatchTextNames(sourceRoot, sourcePaths);
            if (!string.IsNullOrEmpty(outputTextPath))
            {
                EnsureDirectory(outputTextPath);
                File.WriteAllText(outputTextPath, "", settings.FileEncoding);
            }

            foreach (string sourcePath in sourcePaths)
            {
                string fileDir = GetOutputDirectory(sourceRoot, sourcePath, null);
                ProcessFile(sourcePath, result, gameFile =>
                {
                    string path = outputTextPath;
                    if (string.IsNullOrEmpty(path))
                        path = Path.Combine(fileDir, Path.GetFileNameWithoutExtension(GetTextFileName(gameFile)) + ".TXT");

                    IEnumerable<string> lines = ExportTextLines(gameFile, settings);
                    if (lines != null)
                    {
                        EnsureDirectory(path);
                        File.AppendAllLines(path, lines, settings.FileEncoding);
                        result.Exported++;
                    }
                }, batchTextNames[sourcePath]);
            }

            if (!string.IsNullOrEmpty(outputTextPath))
                RemoveDuplicateTextRows(outputTextPath, settings.FileEncoding, settings.AggressiveDeduplication);

            return result;
        }

        public static BatchResult ImportText(string sourceRoot, string textPath)
        {
            var result = new BatchResult();
            var settings = LoadTextSettings();
            string[] sourcePaths = EnumerateSourceFiles(sourceRoot).ToArray();
            Dictionary<string, string> batchTextNames = BuildBatchTextNames(sourceRoot, sourcePaths);
            var rowCache = new Dictionary<string, List<string[]>>(StringComparer.CurrentCultureIgnoreCase);
            List<string[]> sharedRows = File.Exists(textPath) ? ReadTextRows(textPath, settings.FileEncoding, rowCache) : null;

            foreach (string sourcePath in sourcePaths)
            {
                bool changed = false;
                var file = OpenSourceFile(sourcePath, result);
                if (file == null)
                    continue;

                file.Tag = batchTextNames[sourcePath];
                string fileDir = Path.GetDirectoryName(sourcePath);
                ProcessGameFile(file, gameFile =>
                {
                    List<string[]> rows = sharedRows;
                    if (rows == null)
                    {
                        string localPath = Path.Combine(fileDir, Path.GetFileNameWithoutExtension(GetTextFileName(gameFile)) + ".TXT");
                        if (!File.Exists(localPath))
                            return;
                        rows = ReadTextRows(localPath, settings.FileEncoding, rowCache);
                    }

                    if (ImportTextRows(gameFile, rows, settings))
                    {
                        changed = true;
                        result.Imported++;
                    }
                });

                if (changed)
                    SaveFile(sourcePath, file);
            }

            return result;
        }

        private static IEnumerable<string> ExportTextLines(GameFile gameFile, TextSettings settings)
        {
            string fileName = GetTextFileName(gameFile);

            if (gameFile.GameData is PTP ptp)
                return ptp.ExportTXT(settings.RemoveSplit, settings.OldEncoding).Select(x => $"{fileName}\t{x}");
            if (gameFile.GameData is BMD bmd)
                return new PTP(bmd).ExportTXT(settings.RemoveSplit, settings.OldEncoding).Select(x => $"{fileName}\t{x}");
            if (gameFile.GameData is CatherineBMD catherineBmd)
                return catherineBmd.ExportText(fileName, settings.RemoveSplit);
            if (gameFile.GameData is MBM mbm)
                return mbm.ExportText(fileName, settings.RemoveSplit);
            if (gameFile.GameData is P5T p5t)
                return p5t.ExportText(fileName, settings.RemoveSplit);
            if (gameFile.GameData is ATF atf)
                return atf.ExportText(fileName, settings.RemoveSplit);
            if (gameFile.GameData is StringList list)
                return list.ExportText();

            return null;
        }

        private static bool ImportTextRows(GameFile gameFile, List<string[]> rows, TextSettings settings)
        {
            string fileName = GetTextFileName(gameFile);

            if (gameFile.GameData is PTP ptp)
                return ImportPTPText(ptp, fileName, rows, settings);
            if (gameFile.GameData is ATF atf)
                return ImportATFText(atf, fileName, rows, settings);
            if (gameFile.GameData is CatherineBMD catherineBmd)
                return ImportCatherineBMDText(catherineBmd, fileName, rows, settings);
            if (gameFile.GameData is MBM mbm)
                return ImportMBMText(mbm, fileName, rows, settings);
            if (gameFile.GameData is P5T p5t)
                return ImportP5TText(p5t, fileName, rows, settings);
            if (gameFile.GameData is BMD bmd)
            {
                var bmdText = new PTP(bmd);
                bmdText.CopyOld2New(settings.OldEncoding);
                if (!ImportPTPText(bmdText, fileName, rows, settings))
                    return false;

                var temp = new BMD(bmdText, settings.NewEncoding);
                temp.IsLittleEndian = bmd.IsLittleEndian;
                gameFile.GameData = temp;
                return true;
            }
            if (gameFile.GameData is StringList list)
            {
                string[][] imported = rows.Where(x => x.Length > 1 && x[1] != "").ToArray();
                if (imported.Length == 0)
                    return false;
                list.ImportText(imported);
                return true;
            }

            return false;
        }

        private static bool ImportPTPText(PTP ptp, string fileName, List<string[]> rows, TextSettings settings)
        {
            if (settings.LineByLine)
            {
                int textColumn = settings.Map[LineMap.Type.NewText];
                if (textColumn < 0)
                    return false;

                string[] importedLines = rows
                    .Where(row => IsMappedToFile(row, fileName, settings.Map) && row.Length > textColumn)
                    .Select(row => row[textColumn])
                    .ToArray();
                if (importedLines.Length == 0)
                    return false;

                ptp.ImportTextLBL(importedLines);
                return true;
            }

            string[][] imported = rows
                .Select(row => TryGetPTPTranslation(row, fileName, settings.Map, out string[] text) ? text : null)
                .Where(x => x != null)
                .ToArray();

            if (imported.Length == 0)
                return false;

            if (settings.AutoWidth > 0)
                ptp.ImportText(imported, settings.CharacterWidths, settings.AutoWidth);
            else
                ptp.ImportText(imported);

            int oldNameColumn = settings.Map[LineMap.Type.OldName];
            int newNameColumn = settings.Map[LineMap.Type.NewName];
            if (oldNameColumn >= 0 && newNameColumn >= 0)
            {
                Dictionary<string, string> names = rows
                    .Where(row => IsMappedToFile(row, fileName, settings.Map) && row.Length > Math.Max(oldNameColumn, newNameColumn))
                    .Where(row => row[newNameColumn] != "")
                    .GroupBy(row => row[oldNameColumn])
                    .ToDictionary(group => group.Key, group => group.First()[newNameColumn]);
                ptp.ImportNames(names, settings.OldEncoding);
            }

            return true;
        }

        private static bool ImportATFText(ATF atf, string fileName, List<string[]> rows, TextSettings settings)
        {
            var imported = new List<(int Index, string Text)>();
            foreach (string[] row in rows)
                if (TryGetATFTranslation(row, fileName, settings.Map, out int index, out string text))
                    imported.Add((index, text));

            if (imported.Count == 0)
                return false;

            atf.ImportTextByIndex(imported, settings.CharacterWidths, settings.AutoWidth);
            return true;
        }

        private static bool ImportCatherineBMDText(CatherineBMD bmd, string fileName, List<string[]> rows, TextSettings settings)
        {
            var imported = new List<(int Index, string Text)>();
            foreach (string[] row in rows)
                if (TryGetCatherineBMDTranslation(row, fileName, settings.Map, out int index, out string text))
                    imported.Add((index, text));

            if (imported.Count == 0)
                return false;

            bmd.ImportTextByIndex(imported, settings.CharacterWidths, settings.AutoWidth);
            return true;
        }

        private static bool ImportMBMText(MBM mbm, string fileName, List<string[]> rows, TextSettings settings)
        {
            var imported = new List<(int Id, string Identifier, string Text)>();
            foreach (string[] row in rows)
                if (TryGetMBMTranslation(row, fileName, settings.Map, out int id, out string identifier, out string text))
                    imported.Add((id, identifier, text));

            if (imported.Count == 0)
                return false;

            mbm.ImportTextByString(imported, settings.CharacterWidths, settings.AutoWidth);
            return true;
        }

        private static bool ImportP5TText(P5T p5t, string fileName, List<string[]> rows, TextSettings settings)
        {
            var imported = new List<(int Index, string Key, string Identifier, string Text)>();
            foreach (string[] row in rows)
                if (TryGetP5TTranslation(row, fileName, settings.Map, out int index, out string key, out string identifier, out string text))
                    imported.Add((index, key, identifier, text));

            if (imported.Count == 0)
                return false;

            p5t.ImportText(imported);
            return true;
        }

        private static bool TryGetPTPTranslation(string[] row, string fileName, LineMap map, out string[] text)
        {
            text = null;
            int messageColumn = map[LineMap.Type.MSGindex];
            int stringColumn = map[LineMap.Type.StringIndex];
            int textColumn = map[LineMap.Type.NewText];
            if (messageColumn >= 0 && stringColumn >= 0 && textColumn >= 0
                && row.Length >= map.MinLength && IsMappedToFile(row, fileName, map) && row[textColumn] != "")
            {
                text = new[] { row[messageColumn], row[stringColumn], row[textColumn] };
                return true;
            }

            if (row.Length >= 3 && int.TryParse(row[0], out _) && row[2] != "")
            {
                text = new[] { row[0], row[1], row[^1] };
                return true;
            }

            return false;
        }

        private static bool TryGetATFTranslation(string[] row, string fileName, LineMap map, out int index, out string text)
        {
            index = -1;
            text = "";

            if (row.Length >= 4 && IsMatchingFileName(row[0], fileName) && int.TryParse(row[1], out index))
            {
                text = row[3];
                return !string.IsNullOrEmpty(text);
            }

            int indexColumn = map[LineMap.Type.StringIndex] >= 0 ? map[LineMap.Type.StringIndex] : map[LineMap.Type.MSGindex];
            int textColumn = map[LineMap.Type.NewText];
            if (indexColumn >= 0 && textColumn >= 0 && row.Length >= map.MinLength && IsMappedToFile(row, fileName, map)
                && int.TryParse(row[indexColumn], out index))
            {
                text = row[textColumn];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= 6 && IsMatchingFileName(row[0], fileName) && int.TryParse(row[2], out index))
            {
                text = row[5];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= 3 && int.TryParse(row[0], out index))
            {
                text = row[^1];
                return !string.IsNullOrEmpty(text);
            }

            return false;
        }

        private static bool TryGetCatherineBMDTranslation(string[] row, string fileName, LineMap map, out int index, out string text)
        {
            index = -1;
            text = "";

            if (row.Length >= 5 && IsMatchingFileName(row[0], fileName) && int.TryParse(row[1], out index))
            {
                text = row[4];
                return !string.IsNullOrEmpty(text);
            }

            return TryGetATFTranslation(row, fileName, map, out index, out text);
        }

        private static bool TryGetMBMTranslation(string[] row, string fileName, LineMap map, out int id, out string identifier, out string text)
        {
            id = -1;
            identifier = "";
            text = "";

            if (row.Length >= 6 && IsMatchingFileName(row[0], fileName) && int.TryParse(row[1], out id))
            {
                identifier = row[2];
                text = row[5];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= 5 && IsMatchingFileName(row[0], fileName) && int.TryParse(row[1], out id))
            {
                identifier = "0";
                text = row[4];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= 4 && IsMatchingFileName(row[0], fileName) && int.TryParse(row[1], out id))
            {
                identifier = "0";
                text = row[3];
                return !string.IsNullOrEmpty(text);
            }

            int messageColumn = map[LineMap.Type.MSGindex];
            int stringColumn = map[LineMap.Type.StringIndex];
            int textColumn = map[LineMap.Type.NewText];
            if (messageColumn >= 0 && stringColumn >= 0 && textColumn >= 0
                && row.Length >= map.MinLength && IsMappedToFile(row, fileName, map) && int.TryParse(row[messageColumn], out id))
            {
                identifier = row[stringColumn];
                text = row[textColumn];
                return !string.IsNullOrEmpty(text);
            }

            return false;
        }

        private static bool TryGetP5TTranslation(string[] row, string fileName, LineMap map,
            out int index, out string key, out string identifier, out string text)
        {
            index = -1;
            key = "";
            identifier = "";
            text = "";

            if (row.Length >= 6 && IsMappedToFile(row, fileName, map) && int.TryParse(row[1], out index))
            {
                key = row[2];
                identifier = row[3];
                text = row[5];
                return true;
            }

            if (row.Length >= map.MinLength)
            {
                int fileNameColumn = map[LineMap.Type.FileName];
                if (fileNameColumn >= 0 && !IsMappedToFile(row, fileName, map))
                    return false;

                int indexColumn = map[LineMap.Type.MSGindex];
                int keyColumn = map[LineMap.Type.StringIndex];
                int textColumn = map[LineMap.Type.NewText];
                int identifierColumn = map[LineMap.Type.MSGname];
                if (indexColumn >= 0 && keyColumn >= 0 && textColumn >= 0
                    && int.TryParse(row[indexColumn], out index))
                {
                    key = row[keyColumn];
                    identifier = identifierColumn >= 0 ? row[identifierColumn] : "";
                    text = row[textColumn];
                    return true;
                }
            }

            if (row.Length >= 2 && int.TryParse(row[0], out index))
            {
                text = row[^1];
                return true;
            }

            return false;
        }

        private static bool IsMappedToFile(string[] row, string fileName, LineMap map)
        {
            int fileColumn = map[LineMap.Type.FileName];
            return fileColumn < 0 || (row.Length > fileColumn && IsMatchingFileName(row[fileColumn], fileName));
        }

        private static void ProcessFile(string sourcePath, BatchResult result, Action<GameFile> action, string batchTextName = null)
        {
            var file = OpenSourceFile(sourcePath, result);
            if (file != null)
            {
                if (!string.IsNullOrEmpty(batchTextName))
                    file.Tag = batchTextName;
                ProcessGameFile(file, action);
            }
        }

        private static void ProcessGameFile(GameFile gameFile, Action<GameFile> action)
        {
            action(gameFile);
            foreach (GameFile subFile in gameFile.GameData.SubFiles)
                ProcessGameFile(subFile, action);
        }

        private static void ExportGameFileImages(GameFile gameFile, string outputDir, BatchResult result)
        {
            if (gameFile.GameData is IImage image)
            {
                string imageName = GetSafePathPart(Path.GetFileNameWithoutExtension(gameFile.Name)) + ".PNG";
                string path = Path.Combine(outputDir, imageName);
                EnsureDirectory(path);
                var source = image.GetBitmap()?.GetBitmapSource();
                if (source != null)
                {
                    ImageTools.SaveToPNG(source, path);
                    result.Exported++;
                }
            }

            if (gameFile.GameData.SubFiles.Count == 0)
                return;

            string containerDir = Path.Combine(outputDir, GetSafePathPart(gameFile.Name));
            foreach (GameFile subFile in gameFile.GameData.SubFiles)
                ExportGameFileImages(subFile, containerDir, result);
        }

        private static void ImportGameFileImages(
            GameFile gameFile,
            string imageRoot,
            string relativeDir,
            Dictionary<string, string> imageFiles,
            BatchResult result,
            ref bool changed)
        {
            if (gameFile.GameData is IImage image)
            {
                string imageName = GetSafePathPart(Path.GetFileNameWithoutExtension(gameFile.Name)) + ".PNG";
                string imagePath = FindImagePath(imageRoot, relativeDir, imageName, imageFiles);
                if (imagePath != null)
                {
                    try
                    {
                        image.SetBitmap(ImageTools.OpenPNG(imagePath).GetBitmap());
                        changed = true;
                        result.Imported++;
                    }
                    catch
                    {
                        result.Failed++;
                    }
                }
            }

            if (gameFile.GameData.SubFiles.Count == 0)
                return;

            string containerDir = CombineRelativePath(relativeDir, GetSafePathPart(gameFile.Name));
            foreach (GameFile subFile in gameFile.GameData.SubFiles)
                ImportGameFileImages(subFile, imageRoot, containerDir, imageFiles, result, ref changed);
        }

        private static GameFile OpenSourceFile(string sourcePath, BatchResult result)
        {
            try
            {
                return GameFormatHelper.OpenFile(sourcePath);
            }
            catch
            {
                result.Failed++;
                return null;
            }
        }

        private static IEnumerable<string> EnumerateSourceFiles(string sourceRoot)
            => Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray();

        private static string GetOutputDirectory(string sourceRoot, string sourcePath, string outputRoot)
        {
            if (string.IsNullOrEmpty(outputRoot))
                return Path.GetDirectoryName(sourcePath);

            string relativeDir = GetRelativeDirectory(sourceRoot, sourcePath);
            return string.IsNullOrEmpty(relativeDir) ? outputRoot : Path.Combine(outputRoot, relativeDir);
        }

        private static string GetRelativeDirectory(string sourceRoot, string sourcePath)
        {
            string relative = Path.GetRelativePath(sourceRoot, sourcePath);
            string dir = Path.GetDirectoryName(relative);
            return dir == "." ? "" : dir;
        }

        private static string CombineRelativePath(string first, string second)
            => string.IsNullOrEmpty(first) ? second : Path.Combine(first, second);

        private static string GetSafePathPart(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '+');

            return name.Replace('/', '+').Replace('\\', '+');
        }

        private static Dictionary<string, string> BuildImageIndex(string imageRoot)
        {
            return Directory.EnumerateFiles(imageRoot, "*.png", SearchOption.AllDirectories)
                .GroupBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.CurrentCultureIgnoreCase);
        }

        private static string FindImagePath(string imageRoot, string relativeDir, string imageName, Dictionary<string, string> imageFiles)
        {
            string mirrored = string.IsNullOrEmpty(relativeDir)
                ? Path.Combine(imageRoot, imageName)
                : Path.Combine(imageRoot, relativeDir, imageName);
            if (File.Exists(mirrored))
                return mirrored;

            return imageFiles.TryGetValue(imageName, out string indexedPath) ? indexedPath : null;
        }

        private static List<string[]> ReadTextRows(string path, Encoding encoding, Dictionary<string, List<string[]>> cache)
        {
            string key = Path.GetFullPath(path);
            if (cache.TryGetValue(key, out List<string[]> rows))
                return rows;

            rows = File.ReadAllLines(path, encoding).Select(x => x.Split('\t')).ToList();
            cache.Add(key, rows);
            return rows;
        }

        private static bool IsMatchingFileName(string value, string fileName)
            => value.Split('|').Any(x => x.Equals(fileName, StringComparison.CurrentCultureIgnoreCase));

        private static string GetTextFileName(GameFile file)
            => file.Tag as string ?? file.Name;

        private static Dictionary<string, string> BuildBatchTextNames(string sourceRoot, string[] sourcePaths)
        {
            var result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var group in sourcePaths.GroupBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                var paths = group.ToArray();
                if (paths.Length == 1)
                {
                    result[paths[0]] = group.Key;
                    continue;
                }

                var oneParent = paths.ToDictionary(x => x, x => GetTrailingRelativePath(sourceRoot, x, 1), StringComparer.CurrentCultureIgnoreCase);
                bool oneParentIsEnough = oneParent.Values.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() == paths.Length;
                foreach (string path in paths)
                    result[path] = oneParentIsEnough ? oneParent[path] : GetTrailingRelativePath(sourceRoot, path, 2);
            }

            return result;
        }

        private static string GetTrailingRelativePath(string sourceRoot, string filePath, int parentCount)
        {
            string[] parts = Path.GetRelativePath(sourceRoot, filePath)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(x => x != "")
                .ToArray();
            int count = Math.Min(parts.Length, parentCount + 1);
            return Path.Combine(parts.Skip(parts.Length - count).ToArray());
        }

        private static void RemoveDuplicateTextRows(string path, Encoding encoding, bool aggressive)
            => TextExportDeduplicator.RemoveDuplicateTextRows(path, encoding, aggressive);

        private static void SaveFile(string sourcePath, GameFile file)
            => File.WriteAllBytes(sourcePath, file.GameData.GetData());

        private static void EnsureDirectory(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        private static TextSettings LoadTextSettings()
        {
            var settings = ApplicationSettings.AppSetting.Default;
            string oldFont = settings.BatchSourceFont;
            string newFont = settings.BatchDestinationFont;
            int autoWidth = settings.BatchAutoWrap ? settings.BatchAutoWidth : 0;
            PersonaEncoding newEncoding = Static.EncodingManager.GetPersonaEncoding(newFont);
            PersonaFont newPersonaFont = autoWidth > 0 ? Static.FontManager.GetPersonaFont(newFont) : null;
            if (autoWidth > 0 && newPersonaFont == null)
                throw new InvalidOperationException($"The selected destination font '{newFont}' has no .fnt file for automatic wrapping.");

            return new TextSettings
            {
                OldEncoding = Static.EncodingManager.GetPersonaEncoding(oldFont),
                NewEncoding = newEncoding,
                RemoveSplit = settings.BatchRemoveSplit,
                Map = new LineMap(settings.BatchUseMap ? settings.BatchMap : "%FN %MSGIND %STRIND %I %I %NEWSTR"),
                AutoWidth = autoWidth,
                CharacterWidths = newPersonaFont?.GetCharWidth(newEncoding),
                LineByLine = settings.BatchLineByLine,
                AggressiveDeduplication = settings.BatchAggressiveDeduplication,
                FileEncoding = GetFileEncoding(settings.BatchUseEncoding ? settings.BatchEncoding : "UTF-8")
            };
        }

        private static Encoding GetFileEncoding(string name)
        {
            switch (name)
            {
                case "UTF-16": return Encoding.Unicode;
                case "UTF-32": return Encoding.UTF32;
#pragma warning disable SYSLIB0001
                case "UTF-7": return new UTF7Encoding();
#pragma warning restore SYSLIB0001
                default: return Encoding.UTF8;
            }
        }

        private class TextSettings
        {
            public PersonaEncoding OldEncoding { get; set; }
            public PersonaEncoding NewEncoding { get; set; }
            public bool RemoveSplit { get; set; }
            public LineMap Map { get; set; }
            public int AutoWidth { get; set; }
            public Dictionary<char, int> CharacterWidths { get; set; }
            public bool LineByLine { get; set; }
            public bool AggressiveDeduplication { get; set; }
            public Encoding FileEncoding { get; set; }
        }
    }

    internal class BatchResult
    {
        public int Exported { get; set; }
        public int Imported { get; set; }
        public int Failed { get; set; }
    }
}
