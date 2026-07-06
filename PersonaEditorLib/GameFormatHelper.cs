using AuxiliaryLibraries.Extensions;
using PersonaEditorLib.Other;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PersonaEditorLib
{
    public static class GameFormatHelper
    {
        public static Dictionary<string, FormatEnum> FileTypeDic = new Dictionary<string, FormatEnum>()
        {
            //Containers
            { ".bin", FormatEnum.BIN },
            { ".abin", FormatEnum.BIN },
            { ".pak",  FormatEnum.BIN },
            { ".pac",  FormatEnum.PAC },
            { ".apk",  FormatEnum.APK },
            { ".paccs", FormatEnum.PAC },
            { ".pacgz", FormatEnum.PAC },
            { ".fontpac", FormatEnum.PAC },
            { ".p00",  FormatEnum.BIN },
            { ".p01",  FormatEnum.BIN },
            { ".arc",  FormatEnum.BIN },
            { ".dds2", FormatEnum.BIN },
            { ".gsd",  FormatEnum.BIN },

            { ".bf",  FormatEnum.BF  },
            { ".pm1", FormatEnum.PM1 },
            { ".bvp", FormatEnum.BVP },
            { ".tbl", FormatEnum.TBL },
            { ".lb", FormatEnum.LB },

            { ".ctd", FormatEnum.FTD },
            { ".ftd", FormatEnum.FTD },
            { ".ttd", FormatEnum.FTD },

            //Graphic containers
            { ".spr", FormatEnum.SPR },
            { ".spr3", FormatEnum.SPR3 },
            { ".spr6", FormatEnum.SPR6 },
            { ".spr4", FormatEnum.SPR4 },
            { ".g1t", FormatEnum.G1T },
            { ".gnf", FormatEnum.GNF },
            { ".file", FormatEnum.G1T },
            { ".tpc", FormatEnum.TPC },
            { ".cmp", FormatEnum.CMP },
            { ".spd", FormatEnum.SPD },

            //Graphic
            { ".fnt", FormatEnum.FNT },
            { ".tmx", FormatEnum.TMX },
            { ".dds", FormatEnum.DDS },
            { ".ctpk", FormatEnum.CTPK },
            { ".amt", FormatEnum.CTPK },
            { ".hip", FormatEnum.HIP },

            //Text
            { ".atf", FormatEnum.ATF },
            { ".bmd", FormatEnum.BMD },
            { ".msg", FormatEnum.BMD },
            { ".ptp", FormatEnum.PTP }
        };

        /// <summary>
        /// Tries to open a file with the specified data type.
        /// </summary>
        /// <param name="name">Name of file</param>
        /// <param name="data">Data of file</param>
        /// <param name="type">Type of file</param>
        /// <returns>Return ObjectContainer for this file or null if an error occurred.</returns>
        public static GameFile OpenFile(string name, byte[] data, FormatEnum type)
        {
            try
            {
                IGameData Obj;

                if (type == FormatEnum.BIN)
                    Obj = new FileContainer.BIN(data);
                else if (type == FormatEnum.PAC)
                    try
                    {
                        Obj = new FileContainer.PAC(data);
                    }
                    catch
                    {
                        Obj = new DAT(data);
                    }
                else if (type == FormatEnum.APK)
                    Obj = new FileContainer.APK(data);
                else if (type == FormatEnum.SPR)
                    Obj = new SpriteContainer.SPR(data);
                else if (type == FormatEnum.SPR3)
                    Obj = new SpriteContainer.SPR3(data);
                else if (type == FormatEnum.SPR6 || type == FormatEnum.SPR4)
                    Obj = new SpriteContainer.SPR6(data, type);
                else if (type == FormatEnum.G1T)
                    Obj = new SpriteContainer.G1T(data);
                else if (type == FormatEnum.GNF)
                    Obj = new Sprite.GNF(data);
                else if (type == FormatEnum.TPC)
                    Obj = new SpriteContainer.TPC(data);
                else if (type == FormatEnum.CMP)
                    Obj = new Sprite.DMPBM(data);
                else if (type == FormatEnum.TMX)
                    Obj = new Sprite.TMX(data);
                else if (type == FormatEnum.BF)
                    Obj = new FileContainer.BF(data, name);
                else if (type == FormatEnum.PM1)
                    Obj = new FileContainer.PM1(data);
                else if (type == FormatEnum.CatherineBMD)
                    Obj = new Text.CatherineBMD(data);
                else if (type == FormatEnum.BMD)
                    Obj = new Text.BMD(data);
                else if (type == FormatEnum.ATF)
                    Obj = new Text.ATF(data);
                else if (type == FormatEnum.PTP)
                    Obj = new Text.PTP(data);
                else if (type == FormatEnum.FNT)
                    Obj = new FNT(data);
                else if (type == FormatEnum.FNT0)
                    Obj = new FNT0(data);
                else if (type == FormatEnum.BVP)
                    Obj = new FileContainer.BVP(name, data);
                else if (type == FormatEnum.TBL)
                    try
                    {
                        Obj = new FileContainer.TBL(data, name);
                    }
                    catch
                    {
                        Obj = new FileContainer.BIN(data);
                    }
                else if (type == FormatEnum.LB)
                    Obj = new FileContainer.LB(data);
                else if (type == FormatEnum.FTD)
                    Obj = new FTD(data);
                else if (type == FormatEnum.DDS)
                    try
                    {
                        Obj = new Sprite.DDS(data);
                    }
                    catch
                    {
                        Obj = new Sprite.DDSAtlus(data);
                    }
                else if (type == FormatEnum.CTPK)
                    Obj = new Sprite.CTPK(data);
                else if (type == FormatEnum.HIP)
                    Obj = new Sprite.HIP(data);
                else if (type == FormatEnum.SPD)
                    Obj = new SpriteContainer.SPD(data);
                else
                    Obj = new DAT(data);

                return new GameFile(name, Obj);
            }
            catch
            {
                return null;
            }
        }

        public static GameFile OpenFile(string name, byte[] data)
        {
            var nameFormat = GetFormat(name);
            var format = nameFormat == FormatEnum.SPR4 ? nameFormat : GetFormat(data);
            if (format == FormatEnum.Unknown)
                format = nameFormat;

            return OpenFile(name, data, format);
        }

        public static GameFile OpenFile(string path)
        {
            var file = OpenFile(Path.GetFileName(path), File.ReadAllBytes(path));
            if (file?.GameData is SpriteContainer.TPC tpc)
                tpc.LoadGtxSidecar(Path.ChangeExtension(path, ".gtx"));
            else if (file?.GameData is Sprite.HIP hip)
                hip.LoadAbcSidecar(Path.ChangeExtension(path, ".abc"));

            return file;
        }

        public static FormatEnum GetFormat(string name)
        {
            string ext = Path.GetExtension(name).ToLower().TrimEnd(' ');
            if (FileTypeDic.ContainsKey(ext))
                return FileTypeDic[ext];
            else
                return FormatEnum.DAT;
        }

        public static FormatEnum GetFormat(byte[] data)
        {
            if (data.Length >= 0xc)
            {
                ReadOnlySpan<byte> header = data;
                if (HasMagic(header, 0, 0x46, 0x4E, 0x54, 0x30))
                    return FormatEnum.FNT0;
                else if (HasMagic(header, 0, 0x41, 0x54, 0x46, 0x00))
                    return FormatEnum.ATF;

                if (HasMagic(header, 8, 0x31, 0x47, 0x53, 0x4D) || HasMagic(header, 8, 0x4D, 0x53, 0x47, 0x31))
                    return FormatEnum.BMD;
                else if (HasMagic(header, 8, 0x54, 0x4D, 0x58, 0x30))
                    return FormatEnum.TMX;
                else if (HasMagic(header, 8, 0x53, 0x50, 0x52, 0x33))
                    return FormatEnum.SPR3;
                else if (HasMagic(header, 8, 0x53, 0x50, 0x52, 0x30))
                    return FormatEnum.SPR;
                else if (HasMagic(header, 8, 0x46, 0x4C, 0x57, 0x30))
                    return FormatEnum.BF;
                else if (HasMagic(header, 8, 0x50, 0x4D, 0x44, 0x31))
                    return FormatEnum.PM1;
            }

            if (data.Length >= 4)
            {
                ReadOnlySpan<byte> header = data;
                if (HasMagic(header, 0, 0x78, 0x56, 0x34, 0x12))
                    return FormatEnum.CatherineBMD;
                else if (HasMagic(header, 0, 0x46, 0x50, 0x41, 0x43))
                    return FormatEnum.PAC;
                else if (HasMagic(header, 0, 0x43, 0x54, 0x50, 0x4B))
                    return FormatEnum.CTPK;
                else if (HasMagic(header, 0, 0x53, 0x50, 0x52, 0x36))
                    return FormatEnum.SPR6;
                else if (HasMagic(header, 0, 0x47, 0x54, 0x31, 0x47))
                    return FormatEnum.G1T;
                else if (HasMagic(header, 0, 0x47, 0x4E, 0x46, 0x20))
                    return FormatEnum.GNF;
                else if (HasMagic(header, 0, 0x48, 0x49, 0x50, 0x00))
                    return FormatEnum.HIP;
            }
            return FormatEnum.Unknown;
        }

        private static bool HasMagic(ReadOnlySpan<byte> data, int offset, byte a, byte b, byte c, byte d)
            => data.Length >= offset + 4
                && data[offset] == a
                && data[offset + 1] == b
                && data[offset + 2] == c
                && data[offset + 3] == d;
    }
}
