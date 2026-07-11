using AuxiliaryLibraries.IO;
using AuxiliaryLibraries.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PersonaEditorLib.SpriteContainer
{
    public class SPR : IGameData
    {
        SPRHeader Header;
        List<int> TextureOffsetList = new List<int>();
        List<int> TexturePaddingList = new List<int>();
        List<int> KeyOffsetList = new List<int>();
        public SPRKeyList KeyList;

        public SPR(Stream stream)
        {
            BinaryReader reader = IOTools.OpenReadFile(stream, IsLittleEndian);

            Open(reader);
        }

        public SPR(string path) : this(File.OpenRead(path))
        {
        }

        public SPR(byte[] data)
        {
            using (MemoryStream MS = new MemoryStream(data))
                Open(IOTools.OpenReadFile(MS, IsLittleEndian));
        }

        private void Open(BinaryReader reader)
        {
            Header = new SPRHeader(reader);

            reader.BaseStream.Seek(Header.TextureOffset, SeekOrigin.Begin);
            for (int i = 0; i < Header.TextureCount; i++)
            {
                reader.ReadUInt32();
                TextureOffsetList.Add(reader.ReadInt32());
            }

            reader.BaseStream.Seek(Header.KeyFrameOffset, SeekOrigin.Begin);
            for (int i = 0; i < Header.KeyFrameCount; i++)
            {
                reader.ReadUInt32();
                KeyOffsetList.Add(reader.ReadInt32());
            }

            KeyList = new SPRKeyList(reader, KeyOffsetList);

            for (int i = 0; i < TextureOffsetList.Count; i++)
            {
                int offset = TextureOffsetList[i];
                int end = i + 1 < TextureOffsetList.Count ? TextureOffsetList[i + 1] : (int)reader.BaseStream.Length;
                if (offset < 0 || end < offset || end > reader.BaseStream.Length)
                    throw new InvalidDataException("SPR: invalid texture offset.");

                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                var texture = GameFormatHelper.OpenFile($"texture_{i}.dds", reader.ReadBytes(end - offset));
                if (texture == null)
                    throw new InvalidDataException("SPR: unsupported texture data.");

                if (texture.GameData is Sprite.TMX tmx)
                    texture.Name = tmx.Comment + ".tmx";
                TexturePaddingList.Add(Math.Max(0, end - offset - texture.GameData.GetSize()));
                SubFiles.Add(texture);
            }
        }

        private void UpdateOffsets(List<int> list, int start)
        {
            list[0] = start;

            for (int i = 1; i < SubFiles.Count; i++)
            {
                start += SubFiles[i - 1].GameData.GetSize();
                start += TexturePaddingList[i - 1];
                list[i] = start;
            }
        }

        public bool IsLittleEndian { get; set; } = true;

        #region IGameFile

        public FormatEnum Type => FormatEnum.SPR;

        public List<GameFile> SubFiles { get; } = new List<GameFile>();

        public int GetSize()
        {
            int returned = 0;

            returned += Header.Size;
            returned += TextureOffsetList.Count * 8;
            returned += KeyOffsetList.Count * 8;
            returned += KeyList.Size;

            int temp = IOTools.Alignment(returned, 16);
            returned += temp == 0 ? 16 : temp;

            returned += (SubFiles[0].GameData as IGameData).GetSize();
            for (int i = 1; i < SubFiles.Count; i++)
            {
                returned += TexturePaddingList[i - 1];
                returned += SubFiles[i].GameData.GetSize();
            }
            if (TexturePaddingList.Count >= SubFiles.Count)
                returned += TexturePaddingList[SubFiles.Count - 1];

            return returned;
        }

        public byte[] GetData()
        {
            byte[] returned;

            using (MemoryStream MS = new MemoryStream())
            {
                BinaryWriter writer = IOTools.OpenWriteFile(MS, IsLittleEndian);

                Header.filesize = GetSize();
                Header.Get(writer);
                foreach (var a in TextureOffsetList)
                {
                    writer.Write((int)0);
                    writer.Write(a);
                }
                foreach (var a in KeyOffsetList)
                {
                    writer.Write((int)0);
                    writer.Write(a);
                }
                KeyList.Get(writer);

                int temp = IOTools.Alignment(writer.BaseStream.Position, 16);
                writer.Write(new byte[temp == 0 ? 16 : temp]);

                UpdateOffsets(TextureOffsetList, (int)writer.BaseStream.Position);

                writer.Write(SubFiles[0].GameData.GetData());
                for (int i = 1; i < SubFiles.Count; i++)
                {
                    writer.Write(new byte[TexturePaddingList[i - 1]]);
                    writer.Write(SubFiles[i].GameData.GetData());
                }
                if (TexturePaddingList.Count >= SubFiles.Count)
                    writer.Write(new byte[TexturePaddingList[SubFiles.Count - 1]]);

                writer.BaseStream.Position = Header.Size;
                foreach (var a in TextureOffsetList)
                {
                    writer.Write((int)0);
                    writer.Write(a);
                }

                returned = MS.ToArray();
            }

            return returned;
        }

        #endregion
    }
}
