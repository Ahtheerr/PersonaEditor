using PersonaEditor.Classes;
using AuxiliaryLibraries.WPF;
using PersonaEditorLib.Text;
using System.Text;

namespace PersonaEditor.ViewModels.Editors
{
    class BMDMsgStrVM : BindingObject
    {
        int sourceFont;
        readonly bool reload;

        public byte[] data { get; private set; }

        public string Text { get; set; }

        // public FlowDocument Document { get; } = new FlowDocument();

        public void Changes(bool save, int destFont)
        {
            if (save)
                data = Text.GetTextBases(reload ? Encoding.UTF8 : Static.EncodingManager.GetPersonaEncoding(sourceFont)).GetByteArray();
            else
            {
                Text = Decode();
                Notify("Text");
            }
        }

        public void Update(int sourceFont)
        {
            this.sourceFont = sourceFont;
            Text = Decode();
            Notify("Text");
        }

        public BMDMsgStrVM(byte[] array, int sourceFont, bool reload = false)
        {
            data = array;
            this.sourceFont = sourceFont;
            this.reload = reload;

            Text = Decode();
            //  Style style = new Style(typeof(Paragraph));
            //  style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
            //  Document.Resources.Add(typeof(Paragraph), style);
            //  Document.Blocks.Add(data.GetTextBaseList().GetDocument(TestClass.personaEncoding, false));
        }

        private string Decode()
        {
            if (reload)
                return data.GetReloadTextBases().GetString(Encoding.UTF8);

            var encoding = Static.EncodingManager.GetPersonaEncoding(sourceFont);
            return data.GetTextBases(encoding).GetString(encoding);
        }
    }
}
