using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using PersonaEditorLib;
using PersonaEditorLib.Text;
using AuxiliaryLibraries.WPF.Wrapper;
using PersonaEditorCMD.ArgumentHandler;
using PersonaEditorLib.Other;

namespace PersonaEditorCMD
{
    class Program
    {
        private static readonly Dictionary<string, List<string[]>> TextImportCache = new Dictionary<string, List<string[]>>();

        static Program()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        static void LoadSetting()
        {
            string setting = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "PersonaEditor.xml");
            if (File.Exists(setting))
            {
                try
                {
                    XDocument xDoc = XDocument.Load(setting, LoadOptions.PreserveWhitespace);
                    XElement Setting = xDoc.Element("Settings");

                    Static.OldFontName = Setting.Element("OldFont").Value;
                    Static.NewFontName = Setting.Element("NewFont").Value;
                }
                catch
                {

                }
            }
            else
                CreateSetting(setting);
        }

        static void CreateSetting(string path)
        {
            try
            {
                XDocument xDoc = new XDocument();
                XElement Document = new XElement("Settings");
                xDoc.Add(Document);

                XElement Setting = new XElement("OldFont", Static.OldFontName);
                Document.Add(Setting);

                Setting = new XElement("NewFont", Static.NewFontName);
                Document.Add(Setting);

                xDoc.Save(path);
            }
            catch
            {

            }
        }

        static void Main(string[] args)
        {
            LoadSetting();
            //Test(args);

            try
            {
                DoSome(args);
            }
            catch (Exception e)
            {
            }
        }

        static void Test(string[] args)
        {
            string testPath = @"d:\Persona 5\DATA_PS3_JAP\";
            var par = new Parameters(new string[][] { new string[] { "/sub" } });
            var files = Directory.EnumerateFiles(testPath, "*.*", SearchOption.AllDirectories).ToArray();
            int index = 0;

            foreach (var filePath in files)
            {
                Console.Write($"{index++}/{files.Length}\r");

                var OpenedFileDir = Path.GetDirectoryName(filePath);
                if (new FileInfo(filePath).Length > 10000000)
                    continue;
                GameFile file = GameFormatHelper.OpenFile(Path.GetFileName(filePath), File.ReadAllBytes(filePath));
                if (file != null)
                {
                    SubFileAction((a, b, c, d) =>
                    {
                        try
                        {
                            if (a.GameData is BMD bmd)
                            {
                                PTP ptp = new PTP(bmd);
                                var newName = a.Name.Replace('/', '+');
                                string path = Path.Combine(c, Path.GetFileNameWithoutExtension(newName) + ".TXT");

                                var exp = ptp.ExportTXT(true, Static.OldEncoding());
                                File.WriteAllLines(path, exp);
                            }
                        }
                        catch { }
                    }, file, "", OpenedFileDir, par);
                }
            }
        }

        static void DoSome(string[] args)
        {
            ArgumentsWork argwrk = new ArgumentsWork(args);
            if (argwrk.OpenedFile == "")
                return;

            if (argwrk.IsDirectory)
            {
                TextImportCache.Clear();
                ProcessDirectory(argwrk);
                TextImportCache.Clear();
                return;
            }

            GameFile file = GameFormatHelper.OpenFile(Path.GetFileName(argwrk.OpenedFile), File.ReadAllBytes(argwrk.OpenedFile));

            if (file != null)
                ProcessFile(file, argwrk.ArgumentList, argwrk.OpenedFileDir);
        }

        static void ProcessDirectory(ArgumentsWork argwrk)
        {
            string[] filePaths = Directory.EnumerateFiles(argwrk.OpenedFile, "*", SearchOption.AllDirectories).ToArray();
            Dictionary<string, string> batchTextNames = BuildBatchTextNames(argwrk.OpenedFile, filePaths);

            PrepareDirectoryExport(argwrk.ArgumentList);

            foreach (string filePath in filePaths)
            {
                try
                {
                    GameFile file = GameFormatHelper.OpenFile(Path.GetFileName(filePath), File.ReadAllBytes(filePath));
                    if (file != null)
                        ProcessFile(file, argwrk.ArgumentList, Path.GetDirectoryName(filePath), batchTextNames[filePath]);
                }
                catch
                {
                }
            }

            FinalizeDirectoryExport(argwrk.ArgumentList);
        }

        static void PrepareDirectoryExport(IEnumerable<Argument> commands)
        {
            foreach (var command in commands)
                if (command.Command == CommandType.Export && command.Type == CommandSubType.Text && command.Value != "")
                    File.WriteAllText(command.Value, "");
        }

        static void FinalizeDirectoryExport(IEnumerable<Argument> commands)
        {
            foreach (var command in commands)
                if (command.Command == CommandType.Export && command.Type == CommandSubType.Text && command.Value != "" && File.Exists(command.Value))
                    RemoveDuplicateTextRows(command.Value, command.Parameters.FileEncoding, command.Parameters.AggressiveDeduplication);
        }

        static void ProcessFile(GameFile file, IEnumerable<Argument> commands, string openedFileDir, string batchTextName = null)
        {
            if (!string.IsNullOrEmpty(batchTextName))
                file.Tag = batchTextName;

            foreach (var command in commands)
                ProcessCommand(file, command, openedFileDir);
        }

        static void ProcessCommand(GameFile file, Argument command, string openedFileDir)
        {
            Action<GameFile, string, string, Parameters> action = null;
            if (command.Command == CommandType.Export)
            {
                if (command.Type == CommandSubType.Image)
                    action = ExportImage;
                else if (command.Type == CommandSubType.Table)
                    action = ExportTable;
                else if (command.Type == CommandSubType.All)
                    ExportAll(file, openedFileDir);
                else if (command.Type == CommandSubType.PTP)
                    action = ExportPTP;
                else if (command.Type == CommandSubType.Text)
                    action = ExportText;
                else
                    action = ExportByType;
            }
            else if (command.Command == CommandType.Import)
            {
                if (command.Type == CommandSubType.Image)
                    action = ImportImage;
                else if (command.Type == CommandSubType.Table)
                    action = ImportTable;
                else if (command.Type == CommandSubType.All)
                    ImportAll(file, openedFileDir);
                else if (command.Type == CommandSubType.PTP)
                    action = ImportPTP;
                else if (command.Type == CommandSubType.Text)
                    action = ImportText;
            }
            else if (command.Command == CommandType.Save)
                SaveFile(file, command.Value, openedFileDir, command.Parameters);

            if (action != null)
                SubFileAction(action, file, command.Value, openedFileDir, command.Parameters);
        }

        static void ExportImage(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            string path = Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFile.Name) + ".PNG");
            PersonaEditorTools.SaveImageFile(objectFile, path);
        }

        static void ImportImage(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            if (objectFile.GameData is IImage image)
            {
                if (parameters.Size >= 0)
                    if (objectFile.GameData is FNT fnt)
                        fnt.Resize(parameters.Size);

                string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFile.Name) + ".PNG") : value;
                if (File.Exists(path))
                    image.SetBitmap(AuxiliaryLibraries.WPF.Tools.ImageTools.OpenPNG(path).GetBitmap());
            }
        }

        static void ExportTable(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            if (objectFile.GameData is ITable table)
            {
                string path = Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFile.Name) + ".XML");
                table.GetTable().Save(path);
            }
        }

        static void ImportTable(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            if (objectFile.GameData is ITable table)
            {
                string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFile.Name) + ".XML") : value;
                if (File.Exists(path))
                    table.SetTable(XDocument.Load(path));
            }
        }

        static void ExportPTP(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            if (objectFile.GameData is BMD bmd)
            {
                string path = Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFile.Name.Replace('/', '+')) + ".PTP");
                PTP PTP = new PTP(bmd);
                if (parameters.CopyOld2New)
                    PTP.CopyOld2New(GetOldEncoding(bmd));
                File.WriteAllBytes(path, PTP.GetData());
            }
        }

        static void ImportPTP(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            if (objectFile.GameData is BMD bmd)
            {
                string path = Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFile.Name.Replace('/', '+')) + ".PTP");
                if (File.Exists(path))
                {
                    PTP PTP = new PTP(File.ReadAllBytes(path));
                    var temp = new BMD(PTP, GetNewEncoding(bmd));
                    temp.IsLittleEndian = bmd.IsLittleEndian;
                    temp.IsReload = bmd.IsReload;
                    objectFile.GameData = temp;
                }
            }
        }

        static void ExportText(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            string objectFileName = GetTextFileName(objectFile);

            if (objectFile.GameData is PTP ptp)
            {
                ExportPTPText(ptp, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is ATF atf)
            {
                ExportATFText(atf, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is CatherineBMD catherineBmd)
            {
                ExportCatherineBMDText(catherineBmd, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is MBM mbm)
            {
                ExportMBMText(mbm, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is P5T p5t)
            {
                ExportP5TText(p5t, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is BMD bmd)
            {
                ExportPTPText(new PTP(bmd), objectFileName, value, openedFileDir, parameters, GetOldEncoding(bmd));
            }
            else if (objectFile.GameData is StringList strlst)
            {
                string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;
                string[] exp = strlst.ExportText();

                File.AppendAllLines(path, exp);
            }
        }

        static void ImportText(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            string objectFileName = GetTextFileName(objectFile);

            if (objectFile.GameData is PTP ptp)
            {
                ImportPTPText(ptp, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is ATF atf)
            {
                ImportATFText(atf, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is CatherineBMD catherineBmd)
            {
                ImportCatherineBMDText(catherineBmd, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is MBM mbm)
            {
                ImportMBMText(mbm, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is P5T p5t)
            {
                ImportP5TText(p5t, objectFileName, value, openedFileDir, parameters);
            }
            else if (objectFile.GameData is BMD bmd)
            {
                PTP bmdText = new PTP(bmd);
                bmdText.CopyOld2New(GetOldEncoding(bmd));

                if (ImportPTPText(bmdText, objectFileName, value, openedFileDir, parameters))
                {
                    var temp = new BMD(bmdText, GetNewEncoding(bmd));
                    temp.IsLittleEndian = bmd.IsLittleEndian;
                    temp.IsReload = bmd.IsReload;
                    objectFile.GameData = temp;
                }
            }
            else if (objectFile.GameData is StringList strlst)
            {
                string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;
                if (File.Exists(path))
                {
                    string[][] importedtext = GetTextRows(path, parameters.FileEncoding).
                        Where(x => x.Length > 1 && x[1] != "").ToArray();
                    strlst.ImportText(importedtext);
                }
            }
        }

        static void ExportPTPText(PTP ptp, string objectFileName, string value, string openedFileDir,
            Parameters parameters, Encoding oldEncoding = null)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;
            var exp = ptp.ExportTXT(parameters.RemoveSplit, oldEncoding ?? Static.OldEncoding()).Select(x => $"{objectFileName}\t{x}");

            File.AppendAllLines(path, exp);
        }

        static Encoding GetOldEncoding(BMD bmd)
            => bmd.IsReload ? Encoding.UTF8 : Static.OldEncoding();

        static Encoding GetNewEncoding(BMD bmd)
            => bmd.IsReload ? Encoding.UTF8 : Static.NewEncoding();

        static void ExportATFText(ATF atf, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;
            File.AppendAllLines(path, atf.ExportText(objectFileName, parameters.RemoveSplit));
        }

        static void ExportCatherineBMDText(CatherineBMD bmd, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;
            File.AppendAllLines(path, bmd.ExportText(objectFileName, parameters.RemoveSplit));
        }

        static void ExportMBMText(MBM mbm, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;
            File.AppendAllLines(path, mbm.ExportText(objectFileName, parameters.RemoveSplit));
        }

        static void ExportP5TText(P5T p5t, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;
            File.AppendAllLines(path, p5t.ExportText(objectFileName, parameters.RemoveSplit));
        }

        static bool ImportATFText(ATF atf, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;

            if (!File.Exists(path))
                return false;

            var rows = GetTextRows(path, parameters.FileEncoding);

            LineMap map = new LineMap(parameters.Map);
            List<(int Index, string Text)> imported = new List<(int Index, string Text)>();

            foreach (var row in rows)
            {
                if (TryGetATFTranslation(row, objectFileName, map, out int index, out string text))
                    imported.Add((index, text));
            }

            if (parameters.Width > 0)
                atf.ImportTextByIndex(imported, Static.NewFont().GetCharWidth(Static.NewEncoding()), parameters.Width);
            else
                atf.ImportTextByIndex(imported);
            return true;
        }

        static bool ImportCatherineBMDText(CatherineBMD bmd, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;

            if (!File.Exists(path))
                return false;

            var rows = GetTextRows(path, parameters.FileEncoding);
            var imported = new List<(int Index, string Text)>();

            foreach (var row in rows)
                if (TryGetCatherineBMDTranslation(row, objectFileName, out int index, out string text))
                    imported.Add((index, text));

            if (parameters.Width > 0)
                bmd.ImportTextByIndex(imported, Static.NewFont().GetCharWidth(Static.NewEncoding()), parameters.Width);
            else
                bmd.ImportTextByIndex(imported);
            return true;
        }

        static bool ImportMBMText(MBM mbm, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;

            if (!File.Exists(path))
                return false;

            var rows = GetTextRows(path, parameters.FileEncoding);
            LineMap map = new LineMap(parameters.Map);
            var imported = new List<(int Id, string Identifier, string Text)>();

            foreach (var row in rows)
                if (TryGetMBMTranslation(row, objectFileName, map, out int id, out string identifier, out string text))
                    imported.Add((id, identifier, text));

            if (parameters.Width > 0)
                mbm.ImportTextByString(imported, Static.NewFont().GetCharWidth(Static.NewEncoding()), parameters.Width);
            else
                mbm.ImportTextByString(imported);
            return true;
        }

        static bool ImportP5TText(P5T p5t, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;

            if (!File.Exists(path))
                return false;

            List<string[]> rows = GetTextRows(path, parameters.FileEncoding);
            LineMap map = new LineMap(parameters.Map);
            var imported = new List<(int Index, string Key, string Identifier, string Text)>();
            foreach (string[] row in rows)
                if (TryGetP5TTranslation(row, objectFileName, map, out int index, out string key, out string identifier, out string text))
                    imported.Add((index, key, identifier, text));

            if (imported.Count == 0)
                return false;

            p5t.ImportText(imported);
            return true;
        }

        static bool TryGetATFTranslation(string[] row, string objectFileName, LineMap map, out int index, out string text)
        {
            index = -1;
            text = "";

            if (row.Length >= 4 && IsMatchingFileName(row[0], objectFileName))
            {
                if (int.TryParse(row[1], out index))
                {
                    text = row[3];
                    return !string.IsNullOrEmpty(text);
                }
            }

            if (row.Length >= map.MinLength)
            {
                int fileNameColumn = map[LineMap.Type.FileName];
                if (fileNameColumn >= 0 && !IsMatchingFileName(row[fileNameColumn], objectFileName))
                    return false;

                int indexColumn = map[LineMap.Type.StringIndex] >= 0 ? map[LineMap.Type.StringIndex] : map[LineMap.Type.MSGindex];
                int textColumn = map[LineMap.Type.NewText];
                if (indexColumn >= 0 && textColumn >= 0 && int.TryParse(row[indexColumn], out index))
                {
                    text = row[textColumn];
                    return !string.IsNullOrEmpty(text);
                }
            }

            if (row.Length >= 3 && int.TryParse(row[0], out index))
            {
                text = row[^1];
                return !string.IsNullOrEmpty(text);
            }

            return false;
        }

        static bool TryGetCatherineBMDTranslation(string[] row, string objectFileName, out int index, out string text)
        {
            index = -1;
            text = "";

            if (row.Length >= 5 && IsMatchingFileName(row[0], objectFileName) && int.TryParse(row[1], out index))
            {
                text = row[4];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= 4 && IsMatchingFileName(row[0], objectFileName) && int.TryParse(row[1], out index))
            {
                text = row[3];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= 3 && int.TryParse(row[0], out index))
            {
                text = row[^1];
                return !string.IsNullOrEmpty(text);
            }

            return false;
        }

        static bool TryGetMBMTranslation(string[] row, string objectFileName, LineMap map, out int id, out string identifier, out string text)
        {
            id = -1;
            identifier = "";
            text = "";

            if (row.Length >= 6 && IsMatchingFileName(row[0], objectFileName) && int.TryParse(row[1], out id))
            {
                identifier = row[2];
                text = row[5];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= 5 && IsMatchingFileName(row[0], objectFileName) && int.TryParse(row[1], out id))
            {
                identifier = "0";
                text = row[4];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= 4 && IsMatchingFileName(row[0], objectFileName) && int.TryParse(row[1], out id))
            {
                identifier = "0";
                text = row[3];
                return !string.IsNullOrEmpty(text);
            }

            if (row.Length >= map.MinLength)
            {
                int fileNameColumn = map[LineMap.Type.FileName];
                if (fileNameColumn >= 0 && !IsMatchingFileName(row[fileNameColumn], objectFileName))
                    return false;

                int messageColumn = map[LineMap.Type.MSGindex];
                int stringColumn = map[LineMap.Type.StringIndex];
                int textColumn = map[LineMap.Type.NewText];
                if (messageColumn >= 0 && stringColumn >= 0 && textColumn >= 0 && int.TryParse(row[messageColumn], out id))
                {
                    identifier = row[stringColumn];
                    text = row[textColumn];
                    return !string.IsNullOrEmpty(text);
                }
            }

            if (row.Length >= 3 && int.TryParse(row[0], out id))
            {
                identifier = "0";
                text = row[^1];
                return !string.IsNullOrEmpty(text);
            }

            return false;
        }

        static bool TryGetP5TTranslation(string[] row, string objectFileName, LineMap map,
            out int index, out string key, out string identifier, out string text)
        {
            index = -1;
            key = "";
            identifier = "";
            text = "";

            if (row.Length >= 6 && IsMatchingFileName(row[0], objectFileName) && int.TryParse(row[1], out index))
            {
                key = row[2];
                identifier = row[3];
                text = row[5];
                return true;
            }

            if (row.Length >= map.MinLength)
            {
                int fileNameColumn = map[LineMap.Type.FileName];
                if (fileNameColumn >= 0 && !IsMatchingFileName(row[fileNameColumn], objectFileName))
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

        static bool ImportPTPText(PTP ptp, string objectFileName, string value, string openedFileDir, Parameters parameters)
        {
            string path = value == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFileName) + ".TXT") : value;

            if (!File.Exists(path))
                return false;

            List<string[]> import = GetTextRows(path, parameters.FileEncoding);
            LineMap MAP = new LineMap(parameters.Map);

            if (parameters.LineByLine)
            {
                if (MAP[LineMap.Type.NewText] >= 0)
                {
                    string[] importedText = import
                        .Select(x => x[MAP[LineMap.Type.NewText]])
                        .ToArray();
                    ptp.ImportTextLBL(importedText);
                }
            }
            else
            {
                if (MAP[LineMap.Type.FileName] >= 0
                    & MAP[LineMap.Type.MSGindex] >= 0
                    & MAP[LineMap.Type.StringIndex] >= 0
                    & MAP[LineMap.Type.NewText] >= 0)
                {
                    string[][] importedText = import
                        .Where(x => x.Length >= MAP.MinLength)
                        .Where(x => IsMatchingFileName(x[MAP[LineMap.Type.FileName]], objectFileName))
                        .Where(x => x[MAP[LineMap.Type.NewText]] != "")
                        .Select(x => new string[]
                        {
                            x[MAP[LineMap.Type.MSGindex]],
                            x[MAP[LineMap.Type.StringIndex]],
                            x[MAP[LineMap.Type.NewText]]
                        })
                        .ToArray();

                    if (parameters.Width > 0)
                    {
                        var charWidth = Static.NewFont().GetCharWidth(Static.NewEncoding());
                        ptp.ImportText(importedText, charWidth, parameters.Width);
                    }
                    else
                        ptp.ImportText(importedText);
                }
            }

            if (MAP[LineMap.Type.OldName] >= 0 & MAP[LineMap.Type.NewName] >= 0)
            {
                Dictionary<string, string> importedText = import
                        .Where(x => x.Length >= MAP.MinLength)
                        .GroupBy(x => x[MAP[LineMap.Type.OldName]])
                        .ToDictionary(x => x.Key, x => x.First()[MAP[LineMap.Type.NewName]]);
                ptp.ImportNames(importedText, Static.OldEncoding());
            }

            return true;
        }

        static List<string[]> GetTextRows(string path, Encoding encoding)
        {
            string key = $"{Path.GetFullPath(path)}|{encoding.WebName}";
            if (!TextImportCache.TryGetValue(key, out List<string[]> rows))
            {
                rows = File.ReadAllLines(path, encoding).Select(x => x.Split('\t')).ToList();
                TextImportCache.Add(key, rows);
            }

            return rows;
        }

        static bool IsMatchingFileName(string value, string objectFileName)
        {
            return value.Split('|').Any(x => x.Equals(objectFileName, StringComparison.CurrentCultureIgnoreCase));
        }

        static string GetTextFileName(GameFile file)
            => file.Tag as string ?? file.Name;

        static Dictionary<string, string> BuildBatchTextNames(string sourceRoot, string[] filePaths)
        {
            var result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var group in filePaths.GroupBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
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

        static string GetTrailingRelativePath(string sourceRoot, string filePath, int parentCount)
        {
            string[] parts = Path.GetRelativePath(sourceRoot, filePath)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(x => x != "")
                .ToArray();
            int count = Math.Min(parts.Length, parentCount + 1);
            return Path.Combine(parts.Skip(parts.Length - count).ToArray());
        }

        static void RemoveDuplicateTextRows(string path, Encoding encoding, bool aggressive)
            => TextExportDeduplicator.RemoveDuplicateTextRows(path, encoding, aggressive);

        static void ExportByType(GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            foreach (var a in objectFile.GameData.SubFiles)
            {
                string path = Path.Combine(openedFileDir, a.Name.Replace('/', '+'));
                if (objectFile.GameData.Type == GetFileType(value))
                    File.WriteAllBytes(path, objectFile.GameData.GetData());
            }
        }

        static void SubFileAction(Action<GameFile, string, string, Parameters> action, GameFile objectFile, string value, string openedFileDir, Parameters parameters)
        {
            action.Invoke(objectFile, value, openedFileDir, parameters);

            if (parameters.Sub)
                foreach (var a in objectFile.GameData.SubFiles)
                    SubFileAction(action, a, value, openedFileDir, parameters);
        }

        static FormatEnum GetFileType(string type)
        {
            if (Enum.TryParse(type, out FormatEnum formatEnum))
                return formatEnum;
            else
                return FormatEnum.Unknown;
        }

        static void ExportAll(GameFile objectFile, string openedFileDir)
        {
            foreach (var a in objectFile.GameData.SubFiles)
            {
                string fileName = objectFile.GameData.Type == FormatEnum.APK ? a.Name : a.Name.Replace('/', '+');
                string newpath = Path.Combine(openedFileDir, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(newpath));
                File.WriteAllBytes(newpath, a.GameData.GetData());
            }
        }

        static void ImportAll(GameFile objectFile, string openedFileDir)
        {
            foreach (var item in objectFile.GameData.SubFiles)
            {
                string fileName = objectFile.GameData.Type == FormatEnum.APK ? item.Name : item.Name.Replace('/', '+');
                string newpath = Path.Combine(openedFileDir, fileName);
                FormatEnum fileType = item.GameData.Type;

                if (File.Exists(newpath))
                {
                    var file = GameFormatHelper.OpenFile(objectFile.Name, File.ReadAllBytes(newpath), fileType);
                    if (file != null)
                        item.GameData = file.GameData;
                }
            }
        }

        static void SaveFile(GameFile objectFile, string savePath, string openedFileDir, Parameters parameters)
        {
            if (objectFile.GameData is PTP ptp)
            {
                if (parameters.AsBMD)
                {
                    string path = savePath == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFile.Name) + ".BMD") : savePath;
                    Encoding encoding = Static.NewEncoding();

                    BMD bmd = new BMD(objectFile.GameData as PTP, encoding);
                    File.WriteAllBytes(path, bmd.GetData());
                }
                else
                {
                    string path = savePath == "" ? Path.Combine(openedFileDir, objectFile.Name) : savePath;
                    File.WriteAllBytes(path, ptp.GetData());
                }
            }
            else
            {
                string path = savePath == "" ? Path.Combine(openedFileDir, Path.GetFileNameWithoutExtension(objectFile.Name) + (parameters.Overwrite ? "" : "(NEW)") + Path.GetExtension(objectFile.Name)) : savePath;
                File.WriteAllBytes(path, objectFile.GameData.GetData());
            }
        }
    }
}
