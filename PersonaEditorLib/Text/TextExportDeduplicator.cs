using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PersonaEditorLib.Text
{
    public static class TextExportDeduplicator
    {
        public static void RemoveDuplicateTextRows(string path, Encoding encoding, bool aggressive)
        {
            string[] lines = File.ReadAllLines(path, encoding);
            string[] result = aggressive ? DeduplicateRows(lines) : DeduplicateFiles(lines);
            File.WriteAllLines(path, result, encoding);
        }

        private static string[] DeduplicateFiles(string[] lines)
        {
            var fileRows = new Dictionary<string, List<string[]>>(StringComparer.CurrentCultureIgnoreCase);
            var fileOrder = new List<string>();
            var singleRows = new Dictionary<string, string>();

            foreach (string line in lines)
            {
                string[] columns = line.Split('\t');
                if (columns.Length <= 1)
                {
                    singleRows.TryAdd(line, line);
                    continue;
                }

                if (!fileRows.TryGetValue(columns[0], out List<string[]> rows))
                {
                    rows = new List<string[]>();
                    fileRows.Add(columns[0], rows);
                    fileOrder.Add(columns[0]);
                }

                rows.Add(columns);
            }

            var outputGroups = new List<List<string[]>>();
            var signatures = new Dictionary<string, List<string[]>>();
            foreach (string fileName in fileOrder)
            {
                List<string[]> rows = fileRows[fileName];
                string signature = string.Join("\n", rows.Select(row => string.Join('\t', row.Skip(1))));
                if (signatures.TryGetValue(signature, out List<string[]> existingRows))
                {
                    foreach (string[] row in existingRows)
                        row[0] = MergeFileNames(row[0], fileName);
                }
                else
                {
                    signatures.Add(signature, rows);
                    outputGroups.Add(rows);
                }
            }

            return outputGroups.SelectMany(group => group)
                .Select(row => string.Join('\t', row))
                .Concat(singleRows.Values)
                .ToArray();
        }

        private static string[] DeduplicateRows(string[] lines)
        {
            var rows = new Dictionary<string, string[]>();
            var outputRows = new List<string[]>();
            var singleRows = new HashSet<string>();
            var outputSingles = new List<string>();

            foreach (string line in lines)
            {
                string[] columns = line.Split('\t');
                if (columns.Length <= 1)
                {
                    if (singleRows.Add(line))
                        outputSingles.Add(line);
                    continue;
                }

                string key = GetAggressiveKey(columns);
                if (rows.TryGetValue(key, out string[] existing))
                    existing[0] = MergeFileNames(existing[0], columns[0]);
                else
                {
                    rows.Add(key, columns);
                    outputRows.Add(columns);
                }
            }

            return outputRows.Select(row => string.Join('\t', row))
                .Concat(outputSingles)
                .ToArray();
        }

        private static string GetAggressiveKey(string[] columns)
        {
            if (columns.Length >= 6)
                return string.Join('\t', "PTP", columns[2], columns[3], columns[4]);
            if (columns.Length >= 5)
                return string.Join('\t', "IndexedName", columns[1], columns[2], columns[3]);
            if (columns.Length >= 4)
                return string.Join('\t', "Indexed", columns[1], columns[2]);
            return string.Join('\t', columns.Skip(1));
        }

        private static string MergeFileNames(string first, string second)
        {
            return string.Join("|", first.Split('|')
                .Concat(second.Split('|'))
                .Where(x => x != "")
                .Distinct(StringComparer.CurrentCultureIgnoreCase));
        }
    }
}
