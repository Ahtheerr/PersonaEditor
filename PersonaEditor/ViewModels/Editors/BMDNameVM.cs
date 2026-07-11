using AuxiliaryLibraries.WPF;
using PersonaEditor.Classes;
using PersonaEditorLib.Text;
using System.Linq;

namespace PersonaEditor.ViewModels.Editors
{
    class BMDNameVM : BindingObject
    {
        BMDName name;
        int sourceFont;

        public int Index => name.Index;

        public string Name { get; set; }

        public void Changes(bool save, int destFont)
        {
            if (save)
            {
                var encoding = Static.EncodingManager.GetPersonaEncoding(destFont);
                byte[] newNameBytes = Name.GetTextBases(encoding).GetByteArray();
                name.NameBytes = newNameBytes.SequenceEqual(name.NameBytes) ? name.NameBytes : newNameBytes;
            }
            else
            {
                Name = name.NameBytes.GetTextBases(Static.EncodingManager.GetPersonaEncoding(sourceFont)).GetString(Static.EncodingManager.GetPersonaEncoding(sourceFont));
                Notify("Name");
            }
        }

        public void Update(int sourceFont)
        {
            this.sourceFont = sourceFont;
            Name = name.NameBytes.GetTextBases(Static.EncodingManager.GetPersonaEncoding(sourceFont)).GetString(Static.EncodingManager.GetPersonaEncoding(sourceFont));
            Notify("Name");
        }

        public BMDNameVM(BMDName name, int sourceFont)
        {
            this.name = name;
            this.sourceFont = sourceFont;
            Name = name.NameBytes.GetTextBases(Static.EncodingManager.GetPersonaEncoding(sourceFont)).GetString(Static.EncodingManager.GetPersonaEncoding(sourceFont));
        }
    }
}
