using AuxiliaryLibraries.WPF;
using PersonaEditor.Classes;
using PersonaEditorLib.Text;
using System.Linq;
using System.Text;

namespace PersonaEditor.ViewModels.Editors
{
    class BMDNameVM : BindingObject
    {
        BMDName name;
        int sourceFont;
        readonly bool reload;

        public int Index => name.Index;

        public string Name { get; set; }

        public void Changes(bool save, int destFont)
        {
            if (save)
            {
                var encoding = reload ? Encoding.UTF8 : Static.EncodingManager.GetPersonaEncoding(destFont);
                byte[] newNameBytes = Name.GetTextBases(encoding).GetByteArray();
                name.NameBytes = newNameBytes.SequenceEqual(name.NameBytes) ? name.NameBytes : newNameBytes;
            }
            else
            {
                Name = Decode();
                Notify("Name");
            }
        }

        public void Update(int sourceFont)
        {
            this.sourceFont = sourceFont;
            Name = Decode();
            Notify("Name");
        }

        public BMDNameVM(BMDName name, int sourceFont, bool reload = false)
        {
            this.name = name;
            this.sourceFont = sourceFont;
            this.reload = reload;
            Name = Decode();
        }

        private string Decode()
        {
            if (reload)
                return Encoding.UTF8.GetString(name.NameBytes);

            var encoding = Static.EncodingManager.GetPersonaEncoding(sourceFont);
            return name.NameBytes.GetTextBases(encoding).GetString(encoding);
        }
    }
}
