using AuxiliaryLibraries.WPF;
using PersonaEditor.Classes;
using PersonaEditorLib.Text;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace PersonaEditor.ViewModels.Editors
{
    class P5TEditorVM : BindingObject, IEditor
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
                else if (Save())
                {
                    isEdit = false;
                    IsReadOnly = true;
                    Notify("IsReadOnly");
                }

                Notify("IsEdit");
            }
        }

        public bool IsReadOnly { get; set; } = true;
        public ObservableCollection<P5TEntryVM> Entries { get; } = new ObservableCollection<P5TEntryVM>();

        public P5TEditorVM(P5T file, string name)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            this.name = name;
            foreach (P5T.P5TEntry entry in file.Entries)
                Entries.Add(new P5TEntryVM(entry));
        }

        public bool Close()
        {
            return !IsEdit || Save();
        }

        private bool Save()
        {
            MessageBoxResult result = MessageBox.Show("Save changes?", name, MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question, MessageBoxResult.Yes);
            if (result == MessageBoxResult.Cancel)
                return false;

            bool save = result == MessageBoxResult.Yes;
            foreach (P5TEntryVM entry in Entries)
                entry.Changes(save);
            return true;
        }
    }

    class P5TEntryVM : BindingObject
    {
        private readonly P5T.P5TEntry entry;
        private string editedText;

        public int Index => entry.Index;
        public string Key => entry.Key;
        public uint Value => entry.Value;
        public string Identifier => entry.Identifier;
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

        public P5TEntryVM(P5T.P5TEntry entry)
        {
            this.entry = entry;
            editedText = entry.CurrentText;
        }

        public void Changes(bool save)
        {
            entry.NewText = save ? editedText : null;
            if (!save)
            {
                editedText = entry.CurrentText;
                Notify("Text");
            }
        }
    }
}
