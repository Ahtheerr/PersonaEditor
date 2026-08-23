using AuxiliaryLibraries.WPF;
using PersonaEditor.Classes;
using PersonaEditorLib.FileContainer;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace PersonaEditor.ViewModels.Editors
{
    class EVEEditorVM : BindingObject, IEditor
    {
        private readonly EVE file;
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
                    SetEntriesReadOnly(false);
                    Notify("IsReadOnly");
                }
                else if (Save())
                {
                    isEdit = false;
                    IsReadOnly = true;
                    SetEntriesReadOnly(true);
                    Notify("IsReadOnly");
                }

                Notify("IsEdit");
            }
        }

        public bool IsReadOnly { get; set; } = true;
        public ushort FormatVersion => file.FormatVersion;
        public ushort CodeTableEnd => file.CodeTableEnd;
        public ushort StringTableStart => file.StringTableStart;
        public ushort TextStart => file.TextStart;
        public ObservableCollection<EVEEntryVM> Entries { get; } = new ObservableCollection<EVEEntryVM>();

        public EVEEditorVM(EVE file, string name)
        {
            this.file = file ?? throw new ArgumentNullException(nameof(file));
            this.name = name;
            foreach (EVEString entry in file.Strings)
                Entries.Add(new EVEEntryVM(entry));
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

            if (result == MessageBoxResult.No)
            {
                file.DiscardChanges();
                foreach (EVEEntryVM entry in Entries)
                    entry.Refresh();
                return true;
            }

            try
            {
                file.GetData();
                return true;
            }
            catch (InvalidDataException exception)
            {
                MessageBox.Show(exception.Message, name, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void SetEntriesReadOnly(bool value)
        {
            foreach (EVEEntryVM entry in Entries)
                entry.SetReadOnly(value);
        }
    }

    class EVEEntryVM : BindingObject
    {
        private readonly EVEString entry;
        private bool isReadOnly = true;

        public int Index => entry.Index;
        public int Offset => entry.Offset;
        public string Kind => entry.Kind.ToString();
        public bool IsEditable => entry.IsEditable;
        public bool IsReadOnly => isReadOnly || !entry.IsEditable;

        public string Text
        {
            get { return entry.Text; }
            set
            {
                if (!IsReadOnly && entry.Text != value)
                {
                    entry.Text = value;
                    Notify("Text");
                }
            }
        }

        public EVEEntryVM(EVEString entry)
        {
            this.entry = entry;
        }

        public void SetReadOnly(bool value)
        {
            isReadOnly = value;
            Notify("IsReadOnly");
        }

        public void Refresh()
        {
            Notify("Text");
        }
    }
}
