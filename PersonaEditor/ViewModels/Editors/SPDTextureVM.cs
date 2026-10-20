using PersonaEditorLib;
using PersonaEditorLib.Sprite;
using PersonaEditorLib.SpriteContainer;
using AuxiliaryLibraries.WPF;
using AuxiliaryLibraries.WPF.Wrapper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PersonaEditor.ViewModels.Editors
{
    class SPDTextureVM : BindingObject
    {
        GameFile texture;

        private BitmapSource _TextureImage = null;
        private Rect _Rect;
        private object _SelectedItem = null;

        public override void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Image")
            {
                if (sender is TMX tmx)
                {
                    TextureImage = tmx.GetBitmap().GetBitmapSource();
                }
            }
        }

        public SPDTextureVM(GameFile dds, IList<SPDKey> keylist, int index)
        {
            if (dds == null)
                throw new ArgumentNullException(nameof(dds));
            if (keylist == null)
                throw new ArgumentNullException(nameof(keylist));

            texture = dds;
            TextureImage = (dds.GameData as IImage).GetBitmap().GetBitmapSource();

            foreach (var a in keylist.Where(x => x.TextureIndex == index))
                KeyList.Add(new SPDKeyVM(a));
        }

        #region PublicProperties

        public ObservableCollection<object> KeyList { get; } = new ObservableCollection<object>();

        public DrawingCollection Drawings { get; } = new DrawingCollection();

        public string Name => texture.Name;

        public BitmapSource TextureImage
        {
            get { return _TextureImage; }
            private set
            {
                _TextureImage = value;
                _Rect = new Rect(0, 0, _TextureImage.Width, _TextureImage.Height);
                Notify("Rect");
                Notify("TextureImage");
                Notify(nameof(SelectedSpriteImage));
            }
        }

        public Rect Rect => _Rect;

        public object SelectedItem
        {
            get { return _SelectedItem; }
            set
            {
                if (ReferenceEquals(_SelectedItem, value))
                    return;
                if (_SelectedItem is SPDKeyVM itt)
                {
                    itt.IsSelected = false;
                    itt.PropertyChanged -= SelectedSprite_PropertyChanged;
                }
                _SelectedItem = value;
                if (_SelectedItem is SPDKeyVM itt2)
                {
                    itt2.IsSelected = true;
                    itt2.PropertyChanged += SelectedSprite_PropertyChanged;
                }
                Notify(nameof(SelectedItem));
                Notify(nameof(SelectedSpriteImage));
            }
        }

        public ImageSource SelectedSpriteImage
        {
            get
            {
                if (!(SelectedItem is SPDKeyVM key) || TextureImage == null || key.X2 <= 0 || key.Y2 <= 0)
                    return null;

                // Keep the entire crop extent, including transparent space outside the texture.
                var crop = new Rect(key.X1, key.Y1, key.X2, key.Y2);
                var drawing = new DrawingGroup();
                drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(crop)));
                var textureDrawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(crop) };
                textureDrawing.Children.Add(new ImageDrawing(TextureImage,
                    new Rect(0, 0, TextureImage.PixelWidth, TextureImage.PixelHeight)));
                drawing.Children.Add(textureDrawing);
                drawing.Freeze();
                return new DrawingImage(drawing);
            }
        }

        private void SelectedSprite_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SPDKeyVM.Rect))
                Notify(nameof(SelectedSpriteImage));
        }

        #endregion PublicProperties
    }
}
