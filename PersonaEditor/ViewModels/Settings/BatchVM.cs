using AuxiliaryLibraries.WPF;
using PersonaEditor.ApplicationSettings;
using System.Collections.ObjectModel;

namespace PersonaEditor.ViewModels.Settings
{
    class BatchVM : BindingObject
    {
        private readonly AppSetting settings = new AppSetting();

        public ReadOnlyObservableCollection<string> FontList => Static.FontManager.FontList;
        public string[] Encodings { get; } = { "UTF-8", "UTF-16", "UTF-32", "UTF-7" };

        public int SourceFont
        {
            get => GetFontIndex(settings.BatchSourceFont);
            set => settings.BatchSourceFont = Static.FontManager.GetPersonaFontName(value);
        }

        public int DestinationFont
        {
            get => GetFontIndex(settings.BatchDestinationFont);
            set => settings.BatchDestinationFont = Static.FontManager.GetPersonaFontName(value);
        }

        public bool RemoveSplit
        {
            get => settings.BatchRemoveSplit;
            set => settings.BatchRemoveSplit = value;
        }

        public bool UseMap
        {
            get => settings.BatchUseMap;
            set => settings.BatchUseMap = value;
        }

        public string Map
        {
            get => settings.BatchMap;
            set => settings.BatchMap = value;
        }

        public bool AutoWrap
        {
            get => settings.BatchAutoWrap;
            set => settings.BatchAutoWrap = value;
        }

        public string AutoWidth
        {
            get => settings.BatchAutoWidth.ToString();
            set
            {
                if (int.TryParse(value, out int width) && width > 0)
                    settings.BatchAutoWidth = width;
            }
        }

        public bool LineByLine
        {
            get => settings.BatchLineByLine;
            set => settings.BatchLineByLine = value;
        }

        public bool UseEncoding
        {
            get => settings.BatchUseEncoding;
            set => settings.BatchUseEncoding = value;
        }

        public int EncodingIndex
        {
            get
            {
                int index = System.Array.IndexOf(Encodings, settings.BatchEncoding);
                return index < 0 ? 0 : index;
            }
            set
            {
                if (value >= 0 && value < Encodings.Length)
                    settings.BatchEncoding = Encodings[value];
            }
        }

        public void Save()
        {
            settings.Save();
            AppSetting.Default.Reload();
        }

        private int GetFontIndex(string name)
        {
            int index = Static.FontManager.GetPersonaFontIndex(name);
            return index < 0 ? 0 : index;
        }
    }
}
