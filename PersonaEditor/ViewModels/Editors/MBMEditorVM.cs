using AuxiliaryLibraries.WPF;
using PersonaEditor.Classes;
using PersonaEditorLib.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace PersonaEditor.ViewModels.Editors
{
    class MBMEditorVM : BindingObject, IEditor
    {
        private readonly string name;
        private bool isEdit;

        public bool IsEdit
        {
            get { return isEdit; }
            set
            {
                if (value)
                {
                    isEdit = true;
                    IsReadOnly = false;
                    Notify("IsReadOnly");
                }
                else
                {
                    if (Save())
                    {
                        isEdit = false;
                        IsReadOnly = true;
                        Notify("IsReadOnly");
                    }
                }

                Notify("IsEdit");
            }
        }

        public bool IsReadOnly { get; set; } = true;
        public ObservableCollection<MBMNameVM> NameList { get; } = new ObservableCollection<MBMNameVM>();
        public ObservableCollection<MBMMessageVM> MsgList { get; } = new ObservableCollection<MBMMessageVM>();

        public MBMEditorVM(MBM mbm, string name)
        {
            if (mbm == null)
                throw new System.ArgumentNullException(nameof(mbm));

            this.name = name;
            foreach (var group in mbm.Entries
                .Where(x => x.HasNamePrefix)
                .GroupBy(x => x.CurrentName, System.StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x.Min(entry => entry.Id)))
            {
                NameList.Add(new MBMNameVM(group.ToArray()));
            }

            foreach (var entry in mbm.Entries)
                MsgList.Add(new MBMMessageVM(entry));
        }

        public bool Close()
        {
            if (IsEdit)
                return Save();

            return true;
        }

        private bool Save()
        {
            var result = MessageBox.Show("Save changes?", name, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (result != MessageBoxResult.Yes && result != MessageBoxResult.No)
                return false;

            bool save = result == MessageBoxResult.Yes;
            foreach (var nameEntry in NameList)
                nameEntry.Changes(save);
            foreach (var msg in MsgList)
            {
                msg.Changes(save);
                msg.RefreshName();
            }

            return true;
        }
    }

    class MBMNameVM : BindingObject
    {
        private readonly IReadOnlyList<MBM.MBMEntry> entries;
        private string editedName;

        public string Index => entries.Count == 1
            ? entries[0].Id.ToString()
            : $"{entries[0].Id} ({entries.Count})";
        public string Name
        {
            get { return editedName; }
            set
            {
                if (editedName != value)
                {
                    editedName = value ?? string.Empty;
                    Notify("Name");
                }
            }
        }

        public void Changes(bool save)
        {
            if (save)
            {
                foreach (var entry in entries)
                    entry.NewName = editedName == entry.Name ? null : editedName;

                editedName = entries[0].CurrentName;
            }
            else
            {
                editedName = entries[0].CurrentName;
                Notify("Name");
            }
        }

        public MBMNameVM(IReadOnlyList<MBM.MBMEntry> entries)
        {
            this.entries = entries;
            editedName = entries[0].CurrentName;
        }
    }

    class MBMMessageVM : BindingObject
    {
        private readonly MBM.MBMEntry entry;

        public int Index => entry.Id;
        public string Name => string.IsNullOrEmpty(entry.CurrentName) ? $"Index {entry.Id}" : $"Index {entry.Id} - {entry.CurrentName}";
        public ObservableCollection<MBMStringVM> StringList { get; } = new ObservableCollection<MBMStringVM>();

        public void RefreshName()
            => Notify("Name");

        public void Changes(bool save)
        {
            foreach (var text in StringList)
                text.Changes(save);
        }

        public MBMMessageVM(MBM.MBMEntry entry)
        {
            this.entry = entry;
            foreach (var text in entry.Strings)
                StringList.Add(new MBMStringVM(text));
        }
    }

    class MBMStringVM : BindingObject
    {
        private readonly MBM.MBMString text;
        private string editedText;

        public string Identifier => text.IdentifierOrIndex;
        public string Text
        {
            get { return editedText; }
            set
            {
                if (editedText != value)
                {
                    editedText = value;
                    Notify("Text");
                }
            }
        }

        public MBMStringVM(MBM.MBMString text)
        {
            this.text = text;
            editedText = string.IsNullOrEmpty(text.NewText) ? text.OldText : text.NewText;
        }

        public void Changes(bool save)
        {
            text.NewText = save ? editedText : string.Empty;
            if (!save)
            {
                editedText = text.OldText;
                Notify("Text");
            }
        }
    }
}
