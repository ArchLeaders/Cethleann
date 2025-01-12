﻿using Cethleann.Compression;
using Cethleann.ManagedFS;
using Cethleann.ManagedFS.Support;
using Cethleann.Structure;
using Cethleann.Unbundler;
using DragonLib.CLI;
using DragonLib.IO;
using System;
using System.IO;
using System.Linq;

namespace Cethleann.DataExporter
{
    public static class Program
    {
        private static void Main(string[] args)
        {
            Logger.PrintVersion("Cethleann");
            var flags = CommandLineFlags.ParseFlags<DataExporterFlags>(CommandLineFlags.PrintHelp, args);
            if (flags == null) return;

            IManagedFS? fs = default;
            if (flags.LINKDATA)
            {
                fs = new Flayn(flags);
                ((Flayn) fs).LoadPatterns();
                foreach (var gamedir in flags.GameDirs) fs.AddDataFS(gamedir);
            }
            else if (flags.LINKARCHIVE)
            {
                fs = new Leonhart(flags);
                foreach (var gamedir in flags.GameDirs) fs.AddDataFS(gamedir);
            }
            else if (flags.LNK)
            {
                fs = new Mitsunari(flags);
                foreach (var gamedir in flags.GameDirs) fs.AddDataFS(gamedir);
            }
            else if (flags.RDB)
            {
                fs = new Nyotengu(flags);
                foreach (var rdb in flags.GameDirs.SelectMany(gamedir => Directory.GetFiles(gamedir, "*.rdb"))) fs.AddDataFS(rdb);
                foreach (var rdb in flags.GameDirs.SelectMany(gamedir => Directory.GetFiles(gamedir, "*.rdb.hash"))) fs.AddDataFS(rdb);

                ((Nyotengu) fs).LoadExtList();
            }
            else if (flags.PAK)
            {
                fs = new Reisalin(flags);
                foreach (var gamedir in flags.GameDirs.SelectMany(gameDir => Directory.GetFiles(gameDir, "*.pak"))) fs.AddDataFS(gamedir);
            }
            else if (flags.PKG)
            {
                YshtolaSettings? settings = flags.GameId switch
                {
                    "DissidiaNT" => new YshtolaDissidiaSettings(),
                    "VenusVacation" => new YshtolaVenusVacationSettings(),
                    _ => default
                };
                if (settings == default)
                {
                    Logger.Error("Cethleann", $"No decryption settings found for {flags.GameId}!");
                    return;
                }

                fs = new Yshtola(flags, settings);
                var yshtola = (Yshtola) fs;
                yshtola.Root = flags.GameDirs.ToArray();
                foreach (var tableName in settings.TableNames) fs.AddDataFS(tableName);
                if (!Directory.Exists(flags.OutputDirectory)) Directory.CreateDirectory(flags.OutputDirectory);
                for (var index = 0; index < yshtola.Tables.Count; index++)
                {
                    var table = yshtola.Tables[index];
                    var type = Path.GetDirectoryName(yshtola.Settings.TableNames[index]);
                    var name = $"manifest-{type ?? "COMMON"}.{flags.GameId.ToLower()}";
                    File.WriteAllBytes(Path.Combine(flags.OutputDirectory, name), table.Buffer.ToArray());
                }

                if (flags.PKGManifestOnly) return;
            }

            if (fs == null)
            {
                Logger.Error("Cethleann", "No FS specified! Prove --linkdata, --pak, --linkarchive, --lnk, --rdb, or --pkg!");
                return;
            }

            if (!flags.NoFilelist) fs.LoadFileList(flags.FileList);
            if (flags.RDBGeneratedFileList && fs is Nyotengu nyotengu)
            {
                nyotengu.SaveGeneratedFileList(flags.FileList);
                return;
            }

            for (var index = 0; index < fs.EntryCount; index++)
            {
                try
                {
                    var data = fs.ReadEntry(index).Span;
                    var dt = data.GetDataType();
                    var ext = UnbundlerLogic.GetExtension(data);
                    var filepath = fs.GetFilename(index, ext, dt) ?? $"{index}.{ext}";
                    while (filepath.StartsWith("\\") || filepath.StartsWith("/")) filepath = filepath.Substring(1);
                    if (flags.PAK && filepath.EndsWith(".gz", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (data[4] == 0x78)
                            data = StreamCompression.Decompress(data, new CompressionOptions
                            {
                                Length = -1,
                                Type = DataCompression.Deflate
                            });
                        filepath = filepath.Substring(0, filepath.Length - 3);
                    }

                    var pathBase = $@"{flags.OutputDirectory}\{filepath}";
                    UnbundlerLogic.TryExtractBlob(pathBase, data, false, flags, false);
                }
                catch (Exception e)
                {
                    Logger.Error("Cethleann", e);
#if DEBUG
                    throw;
#endif
                }
            }

            fs.Dispose();
        }
    }
}
