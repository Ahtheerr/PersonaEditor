using PersonaEditorLib;
using AuxiliaryLibraries.WPF;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PersonaEditor.ViewModels.Tools
{
    class SetCharVM : BindingObject
    {
        public class FnMpImg : BindingObject
        {
            public int Index { get; set; } = 0;

            private string _Char = "";
            public string Char
            {
                get { return _Char; }
                set
                {
                    if (_Char != value)
                    {
                        _Char = value;
                        Notify("Char");
                    }
                }
            }

            private BitmapSource _Image;
            public BitmapSource Image
            {
                get { return _Image; }
                set
                {
                    _Image = value;
                    Notify("Image");
                }
            }
        }

        private int _FontSelect = -1;
        public ReadOnlyObservableCollection<string> FontList => Static.FontManager.FontList;

        public int FontSelect
        {
            get { return _FontSelect; }
            set
            {
                if (Save())
                {
                    _FontSelect = value;
                    GlyphListUpdate();
                }
                else { Notify("FontSelect"); }
            }
        }

        public BindingList<FnMpImg> GlyphList { get; } = new BindingList<FnMpImg>();

        public SetCharVM()
        {
            Closing = new RelayCommand(Window_Closing);
            ExportTsv = new RelayCommand(ExportTsvCommand);
            ImportTsv = new RelayCommand(ImportTsvCommand);
            GlyphList.ListChanged += GlyphList_ListChanged;
        }

        private void GlyphList_ListChanged(object sender, ListChangedEventArgs e)
        {
            IsChanged = true;
            GlyphList.ListChanged -= GlyphList_ListChanged;
        }

        bool IsChanged = false;

        private void GlyphListUpdate()
        {
            GlyphList.ListChanged -= GlyphList_ListChanged;
            GlyphList.Clear();
            var font = Static.FontManager.GetPersonaFont(_FontSelect);
            if (font != null)
                foreach (var a in font.DataList)
                {
                    var pallete = new BitmapPalette(font.Palette.Select(x => System.Windows.Media.Color.FromArgb(x.A, x.R, x.G, x.B)).ToArray());
                    var form = AuxiliaryLibraries.WPF.Wrapper.Imaging.AuxToWPF(font.PixelFormat);
                    var temp = new FnMpImg()
                    {
                        Index = a.Key,
                        Image = BitmapSource.Create(font.Width, font.Height, 96, 96, form, pallete, a.Value, (font.PixelFormat.BitsPerPixel * font.Width + 7) / 8)
                    };
                    GlyphList.Add(temp);
                }
            var enc = Static.EncodingManager.GetPersonaEncoding(Static.FontManager.GetPersonaFontName(_FontSelect));
            foreach (var a in GlyphList)
                if (enc.Dictionary.TryGetValue(a.Index, out var value))
                    a.Char = value.ToString();

            GlyphList.ListChanged += GlyphList_ListChanged;
        }

        bool Save()
        {
            if (IsChanged)
            {
                var result = MessageBox.Show("Save changed?", "Save", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);

                if (result == MessageBoxResult.Yes)
                {
                    var fontName = Static.FontManager.GetPersonaFontName(_FontSelect);
                    var sourceDir = Static.FontManager.sourcedir;
                    var mapPath = Path.Combine(sourceDir, fontName + ".FNTMAP");

                    PersonaEncoding personaEncoding = new PersonaEncoding();
                    foreach (var a in GlyphList)
                        if (a.Char.Length > 0)
                            personaEncoding.Add(a.Index, a.Char[0]);

                    personaEncoding.SaveFNTMAP(mapPath);

                    Static.EncodingManager.Reload(fontName);
                }
                else if (result == MessageBoxResult.Cancel)
                    return false;

                IsChanged = false;
                return true;
            }
            else return true;
        }

        public ICommand Closing { get; }
        public ICommand ExportTsv { get; }
        public ICommand ImportTsv { get; }

        void Window_Closing(object arg)
        {
            if (!Save())
                (arg as CancelEventArgs).Cancel = true;
        }

        private void ExportTsvCommand(object arg)
        {
            if (_FontSelect < 0)
                return;

            var dialog = new SaveFileDialog
            {
                Filter = "TSV files (*.tsv)|*.tsv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = Static.FontManager.GetPersonaFontName(_FontSelect) + ".tsv"
            };

            if (dialog.ShowDialog() != true)
                return;

            File.WriteAllLines(dialog.FileName, BuildTsvLines(), new UTF8Encoding(false));
        }

        private string[] BuildTsvLines()
        {
            var glyphsByIndex = GlyphList.ToDictionary(x => x.Index);
            int maxIndex = glyphsByIndex.Count == 0 ? 0 : glyphsByIndex.Keys.Max();
            string[] lines = new string[(maxIndex / 16) + 1];

            for (int row = 0; row < lines.Length; row++)
            {
                string[] columns = new string[16];
                for (int column = 0; column < columns.Length; column++)
                {
                    int index = row * 16 + column;
                    columns[column] = glyphsByIndex.TryGetValue(index, out var glyph) ? EscapeTsvChar(glyph.Char) : "\\u0000";
                }
                lines[row] = string.Join("\t", columns);
            }

            return lines;
        }

        private void ImportTsvCommand(object arg)
        {
            if (_FontSelect < 0)
                return;

            var dialog = new OpenFileDialog
            {
                Filter = "TSV files (*.tsv)|*.tsv|Text files (*.txt)|*.txt|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            ImportTsvFile(dialog.FileName);
        }

        private void ImportTsvFile(string path)
        {
            var glyphsByIndex = GlyphList.ToDictionary(x => x.Index);
            int imported = 0;

            int row = 0;
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line))
                {
                    row++;
                    continue;
                }

                string[] columns = line.Split('\t');
                for (int column = 0; column < columns.Length && column < 16; column++)
                {
                    int index = row * 16 + column;
                    if (!glyphsByIndex.TryGetValue(index, out var glyph))
                        continue;

                    string value = UnescapeTsvChar(columns[column]);
                    glyph.Char = value == "\0" ? "" : value;
                    imported++;
                }
                row++;
            }

            if (imported > 0)
                IsChanged = true;
        }

        private static string EscapeTsvChar(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\\u0000";

            char c = value[0];
            if (c == '\\')
                return "\\\\";
            if (c == '\t')
                return "\\u0009";
            if (c == '\r')
                return "\\u000D";
            if (c == '\n')
                return "\\u000A";
            if (char.IsControl(c) || c == ' ')
                return $"\\u{(int)c:X4}";

            return c.ToString();
        }

        private static string UnescapeTsvChar(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\0";

            if (value == "\\\\")
                return "\\";

            if (value.Length == 6 && value.StartsWith("\\u", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int code))
                return ((char)code).ToString();

            return value.Substring(0, 1);
        }
    }
}
