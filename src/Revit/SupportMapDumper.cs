using System;
using System.IO;
using System.Text;

namespace HangerLayout.Revit
{
    /// <summary>
    /// EXPLORATORY helper for reverse-engineering Autodesk Fabrication's
    /// SUPPORT.MAP file. Decompresses via MapFileHelper, writes the full
    /// hex+ASCII payload to a dump file, and (if the user supplies any)
    /// searches for memorable strings so we can locate spec records in
    /// the binary layout.
    ///
    /// Intent: short-lived. Once we've reverse-engineered the record
    /// schema, this becomes SupportMapReader.cs (a real parser like
    /// SupplierMapReader / MaterialMapReader) and this dumper goes away.
    /// </summary>
    internal static class SupportMapDumper
    {
        public sealed class DumpResult
        {
            public bool   Success      { get; init; }
            public string SourcePath   { get; init; } = "";
            public string DumpPath     { get; init; } = "";
            public int    RawBytes     { get; init; }
            public int    PayloadBytes { get; init; }
            public int    NeedleHits   { get; init; }
            public string Notes        { get; init; } = "";
        }

        /// <summary>
        /// Decompresses <paramref name="supportMapPath"/> and writes a hex+ASCII
        /// dump next to it. Additional <paramref name="needles"/> are searched
        /// case-insensitively in the decompressed payload, with surrounding
        /// context bytes included in the dump.
        /// </summary>
        public static DumpResult Dump(string supportMapPath, string[]? needles = null)
        {
            if (string.IsNullOrWhiteSpace(supportMapPath) || !File.Exists(supportMapPath))
                return new DumpResult { Notes = $"File not found: {supportMapPath}" };

            byte[] raw;
            try { raw = File.ReadAllBytes(supportMapPath); }
            catch (Exception ex) { return new DumpResult { Notes = $"Read failed: {ex.Message}" }; }

            byte[]? payload = MapFileHelper.TryDecompress(raw, out string headerInfo, out string zlibInfo);
            if (payload == null)
            {
                return new DumpResult
                {
                    SourcePath = supportMapPath,
                    RawBytes   = raw.Length,
                    Notes      = $"Decompress failed. Header: {headerInfo}. zlib search: {(string.IsNullOrEmpty(zlibInfo) ? "no candidate" : zlibInfo)}",
                };
            }

            // Write the dump next to the source so the user can find it
            // easily. Filename includes a timestamp-free suffix so repeated
            // runs overwrite cleanly.
            string dumpPath = Path.Combine(
                Path.GetDirectoryName(supportMapPath) ?? Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(supportMapPath) + "_dump.txt");

            int hits = 0;
            try
            {
                using var sw = new StreamWriter(dumpPath, append: false, Encoding.UTF8);
                sw.WriteLine($"=== SUPPORT.MAP dump @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                sw.WriteLine($"Source: {supportMapPath}");
                sw.WriteLine($"Raw size: {raw.Length} bytes");
                sw.WriteLine($"Header: {headerInfo}");
                sw.WriteLine($"zlib: {zlibInfo}");
                sw.WriteLine($"Decompressed payload: {payload.Length} bytes");
                sw.WriteLine();

                if (needles != null && needles.Length > 0)
                {
                    sw.WriteLine($"=== Needle search ({needles.Length}) ===");
                    foreach (var needle in needles)
                    {
                        if (string.IsNullOrWhiteSpace(needle)) continue;
                        sw.WriteLine($"\n── searching for '{needle}' ──");
                        int n = MapFileHelper.FindAsciiOccurrences(payload, needle, sw, contextBytes: 96);
                        sw.WriteLine($"({n} match(es) for '{needle}')");
                        hits += n;
                    }
                    sw.WriteLine();
                }

                sw.WriteLine("=== Full hex+ASCII dump of decompressed payload ===");
                MapFileHelper.DumpHex(sw, payload, payload.Length);
            }
            catch (Exception ex)
            {
                return new DumpResult
                {
                    Success      = false,
                    SourcePath   = supportMapPath,
                    RawBytes     = raw.Length,
                    PayloadBytes = payload.Length,
                    Notes        = $"Dump write failed: {ex.Message}",
                };
            }

            return new DumpResult
            {
                Success      = true,
                SourcePath   = supportMapPath,
                DumpPath     = dumpPath,
                RawBytes     = raw.Length,
                PayloadBytes = payload.Length,
                NeedleHits   = hits,
                Notes        = "OK",
            };
        }

        /// <summary>
        /// Returns the saved Fabrication Database folder, or null if it's
        /// not configured. Used as the starting directory for the file picker
        /// so the user lands directly in the Fab config's Database folder.
        /// </summary>
        public static string? TryGetDatabaseFolder(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                var folder = HangerSettingsStore.GetFabricationDatabaseFolder(doc);
                if (string.IsNullOrWhiteSpace(folder)) return null;
                return Directory.Exists(folder) ? folder : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Best-effort lookup of the active Fabrication configuration's
        /// Database\SUPPORT.MAP path, by reading the saved folder hint
        /// from <see cref="HangerSettingsStore"/>. Falls back to null
        /// if no folder is set or the file is missing.
        /// </summary>
        public static string? TryFindFromSettings(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                var folder = HangerSettingsStore.GetFabricationDatabaseFolder(doc);
                if (string.IsNullOrWhiteSpace(folder)) return null;
                string p = Path.Combine(folder, "SUPPORT.MAP");
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }
    }
}
