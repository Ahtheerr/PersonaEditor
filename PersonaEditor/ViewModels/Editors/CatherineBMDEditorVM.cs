using AuxiliaryLibraries.WPF;
using PersonaEditor.Classes;
using PersonaEditorLib.Text;
using System.Collections.ObjectModel;

namespace PersonaEditor.ViewModels.Editors
{
    class CatherineBMDEditorVM : BindingObject, IEditor
    {
        public ObservableCollection<CatherineBMDEntryVM> Entries { get; } = new ObservableCollection<CatherineBMDEntryVM>();

        public CatherineBMDEditorVM(CatherineBMD bmd)
        {
            if (bmd == null)
                throw new System.ArgumentNullException(nameof(bmd));

            foreach (var entry in bmd.Entries)
                Entries.Add(new CatherineBMDEntryVM(entry));
        }

        public bool Close() => true;
    }

    class CatherineBMDEntryVM : BindingObject
    {
        private readonly CatherineBMD.CatherineBMDEntry entry;
        private string newText;

        public int Index => entry.Index;
        public string Name => entry.Name;
        public string OldText => entry.OldText;

        public string NewText
        {
            get { return newText; }
            set
            {
                if (newText != value)
                {
                    newText = value;
                    entry.NewText = value;
                    Notify("NewText");
                }
            }
        }

        public CatherineBMDEntryVM(CatherineBMD.CatherineBMDEntry entry)
        {
            this.entry = entry;
            newText = entry.NewText;
        }
    }
}
