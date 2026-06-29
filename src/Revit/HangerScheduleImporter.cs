using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using HangerLayout.Models;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Imports a Harris hanger schedule (CSV or .xlsx) and maps it to <see cref="SupportSpec"/>s for the Hanger
    /// Layout dialog. The schedule is keyed by Service + Media + Material + Insulation + Size; each row sets the
    /// spacing, fitting/joint setbacks, hanger type, and rod diameter for one size band.
    ///
    /// XLSX is read via <see cref="System.IO.Packaging"/> (WindowsBase) — NOT System.IO.Compression, which is
    /// banned on net48 in a Revit add-in (journal-verified Revit 2024 conflict) — so this builds for every target.
    /// </summary>
    public static class HangerScheduleImporter
    {
        /// <summary>One schedule row as a case-insensitive header→cell lookup. <see cref="Get"/> trims;
        /// <see cref="GetDouble"/> parses invariant-culture numbers (returns 0 when absent/blank/non-numeric).</summary>
        public sealed class RawRow
        {
            private readonly Dictionary<string, string> _cells;
            public RawRow(Dictionary<string, string> cells) { _cells = cells; }
            public string Get(string column)
                => _cells.TryGetValue((column ?? "").Trim(), out string? v) ? (v ?? "").Trim() : "";
            public double GetDouble(string column)
                => double.TryParse(Get(column), NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? d : 0;
            /// <summary>The header names present — surfaced so the UI can show the user what columns it found.</summary>
            public IEnumerable<string> Columns => _cells.Keys;
        }

        /// <summary>Read a schedule file into raw rows, dispatching on extension (.csv / .xlsx).</summary>
        public static List<RawRow> Read(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ext == ".csv") return ReadCsv(path);
            if (ext == ".xlsx") return ReadXlsx(path);
            throw new NotSupportedException("Hanger schedule import supports .csv and .xlsx files only.");
        }

        // ── CSV (stdlib, quote-aware) ───────────────────────────────────────────────────────────────────
        public static List<RawRow> ReadCsv(string path)
        {
            var rows = new List<RawRow>();
            // NOTE (v1 limitation): File.ReadAllLines splits on newlines BEFORE the quote-aware parse, so a quoted
            // field containing an embedded newline breaks into two fragment rows. Typical schedule exports don't
            // have them; a misaligned fragment is dropped by the blank / MaxSize<=0 guards downstream. Revisit with
            // a streaming reader if a real schedule trips this.
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return rows;
            List<string> headers = ParseCsvLine(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                rows.Add(BuildRow(headers, ParseCsvLine(lines[i])));
            }
            return rows.Where(r => r.Columns.Any()).ToList();
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } // escaped quote
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            fields.Add(sb.ToString());
            return fields;
        }

        // ── XLSX (System.IO.Packaging — first worksheet, first row = headers) ──────────────────────────
        public static List<RawRow> ReadXlsx(string path)
        {
            using (Package pkg = Package.Open(path, FileMode.Open, FileAccess.Read))
            {
                // Shared strings: cells with t="s" carry an index into this table.
                var shared = new List<string>();
                PackagePart? ss = pkg.GetParts().FirstOrDefault(p =>
                    p.Uri.OriginalString.EndsWith("/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
                if (ss != null)
                    foreach (XElement si in XDocument.Load(ss.GetStream()).Root?.Elements() ?? Enumerable.Empty<XElement>())
                        shared.Add(ConcatText(si));

                // First worksheet part.
                PackagePart? wsPart = pkg.GetParts().FirstOrDefault(p =>
                    p.Uri.OriginalString.IndexOf("/worksheets/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    p.Uri.OriginalString.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                if (wsPart == null) return new List<RawRow>();

                // Parse each row into colIndex→value.
                var grid = new List<Dictionary<int, string>>();
                foreach (XElement rowEl in XDocument.Load(wsPart.GetStream())
                                                    .Descendants().Where(e => e.Name.LocalName == "row"))
                {
                    var cells = new Dictionary<int, string>();
                    foreach (XElement c in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
                    {
                        int col = ColIndex((string?)c.Attribute("r") ?? "");
                        if (col < 0) continue;
                        string t = (string?)c.Attribute("t") ?? "";
                        if (t == "inlineStr") { cells[col] = ConcatText(c); continue; }
                        string raw = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
                        cells[col] = (t == "s" && int.TryParse(raw, out int idx) && idx >= 0 && idx < shared.Count)
                            ? shared[idx] : raw;
                    }
                    grid.Add(cells);
                }
                return GridToRows(grid);
            }
        }

        // Concatenate the <t> text runs under a shared-string/inline-string element.
        private static string ConcatText(XElement el)
            => string.Concat(el.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value));

        // "AB12" → zero-based column index (A=0). Returns -1 if no column letters.
        private static int ColIndex(string cellRef)
        {
            int col = 0, letters = 0;
            foreach (char ch in cellRef)
            {
                char u = char.ToUpperInvariant(ch);
                if (u < 'A' || u > 'Z') break;
                col = col * 26 + (u - 'A' + 1);
                letters++;
            }
            return letters == 0 ? -1 : col - 1;
        }

        // First populated grid row = headers (by column index); each later row maps colIndex→header name.
        private static List<RawRow> GridToRows(List<Dictionary<int, string>> grid)
        {
            var rows = new List<RawRow>();
            if (grid.Count == 0) return rows;
            Dictionary<int, string> headerRow = grid[0];
            var headers = new List<string>();
            // Flatten the header row into a dense ordered list by column index.
            int maxCol = headerRow.Keys.Any() ? headerRow.Keys.Max() : -1;
            for (int c = 0; c <= maxCol; c++)
                headers.Add(headerRow.TryGetValue(c, out string? h) ? (h ?? "") : "");
            for (int r = 1; r < grid.Count; r++)
            {
                var fields = new List<string>();
                for (int c = 0; c < headers.Count; c++)
                    fields.Add(grid[r].TryGetValue(c, out string? v) ? (v ?? "") : "");
                if (fields.Any(f => !string.IsNullOrWhiteSpace(f))) rows.Add(BuildRow(headers, fields));
            }
            return rows;
        }

        // Build a RawRow from a header list + a field list (shared by the CSV and XLSX paths).
        private static RawRow BuildRow(List<string> headers, List<string> fields)
        {
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < headers.Count; c++)
            {
                string h = (headers[c] ?? "").Trim();
                if (h.Length == 0) continue;
                cells[h] = c < fields.Count ? fields[c] : "";
            }
            return new RawRow(cells);
        }

        // ── Map raw rows → SupportSpecs ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Group the schedule rows into one <see cref="SupportSpec"/> per Service+Media+Material+Insulation and
        /// add one size band (<see cref="SupportSpecRow"/>) per row. The plumbing (grouping, band assembly,
        /// ascending size sort) is complete; the per-row COLUMN MAPPING is supplied by <see cref="ParseRow"/>.
        /// </summary>
        public static List<SupportSpec> ToSupportSpecs(List<RawRow> rows)
        {
            var byKey = new Dictionary<string, SupportSpec>(StringComparer.OrdinalIgnoreCase);
            var result = new List<SupportSpec>();
            foreach (RawRow row in rows)
            {
                ParsedRow p = ParseRow(row);
                if (string.IsNullOrWhiteSpace(p.Service) && p.MaxSizeInches <= 0) continue; // blank/garbage row

                string key = p.Service + "|" + p.Media + "|" + p.Material + "|" + p.Insulation;
                if (!byKey.TryGetValue(key, out SupportSpec? spec))
                {
                    spec = new SupportSpec
                    {
                        Name = string.IsNullOrWhiteSpace(p.Service) ? "(imported)" : p.Service,
                        Domain = HangerDomain.Pipe, // the Harris schedule is pipe; duct import is a later pass
                        Media = p.Media,
                        Material = p.Material,
                        Insulation = p.Insulation,
                    };
                    byKey[key] = spec;
                    result.Add(spec);
                }
                if (p.MaxSizeInches > 0)
                    spec.Rows.Add(new SupportSpecRow
                    {
                        MaxSizeInches = p.MaxSizeInches,
                        StraightSpacingInches = p.SpacingInches,
                        FittingDistanceInches = p.FittingSetbackInches,
                        DistanceFromJointInches = p.JointSetbackInches,
                        HangerType = p.HangerType,
                        RodDiameterInches = p.RodDiameterInches,
                        DistanceFromAnchorInches = p.AnchorSetbackInches,
                        InsulationInsertType = p.InsulationInsertType,
                        HangerSizeOdInches = p.HangerSizeOdInches,
                    });
            }
            // Size bands ascending so placement picks the smallest band that fits a given pipe size.
            foreach (SupportSpec s in result) s.Rows.Sort((a, b) => a.MaxSizeInches.CompareTo(b.MaxSizeInches));
            return result;
        }

        /// <summary>The values pulled from ONE schedule row, before grouping. Filled by <see cref="ParseRow"/>.</summary>
        private struct ParsedRow
        {
            public string Service, Media, Material, Insulation, HangerType, InsulationInsertType;
            public double MaxSizeInches, SpacingInches, FittingSetbackInches, JointSetbackInches,
                          AnchorSetbackInches, RodDiameterInches, HangerSizeOdInches;
        }

        // Map a schedule row to the parsed fields. Column matching is TOLERANT — each field tries several candidate
        // header names, so a slightly different header still binds. Names are best-guess from the source hanger
        // schedule; the importer surfaces RawRow.Columns so the UI can report what it actually found and we can
        // refine. UNIT ASSUMPTION: spacing + setbacks are FEET in the schedule → converted to inches here; pipe size
        // + rod diameter are already inches. (Confirm against the real schedule; a future settings module will make
        // the column names + units configurable.)
        private static ParsedRow ParseRow(RawRow row) => new ParsedRow
        {
            Service              = First(row, "Service Name", "Service"),
            Media                = First(row, "Media", "System"),
            Material             = First(row, "Pipe Material Description", "Pipe Material", "Material"),
            Insulation           = First(row, "Pipe Insulation Type", "Insulation Type", "Insulation"),
            MaxSizeInches        = Num(row,   "Pipe Size", "Size", "Max Size", "Nominal Size"),
            SpacingInches        = FeetCol(row, "Hanger Spacing", "Spacing", "Max Spacing"),
            FittingSetbackInches = FeetCol(row, "Distance From Fitting", "DistanceFromFitting", "Fitting Distance", "Fitting Setback"),
            // Joint takes its own column when present; "Distance From Anchor" stays as a LAST-RESORT fallback
            // so legacy schedules (pre-split, anchor-only — e.g. the v1.1.0 sample) still drive joint placement.
            JointSetbackInches   = FeetCol(row, "Distance From Joint", "DistanceFromJoint", "Joint Setback",
                                                "Distance From Anchor", "DistanceFromAnchor"),
            AnchorSetbackInches  = FeetCol(row, "Distance From Anchor", "DistanceFromAnchor", "Anchor Setback"),
            HangerType           = First(row, "Hanger Type", "Type"),
            RodDiameterInches    = Num(row,   "Rod Diameter", "Rod Size", "Rod Dia", "Rod"),
            InsulationInsertType = First(row, "Insulation Insert Type", "Insulation Insert", "Insert Type", "Insert"),
            HangerSizeOdInches   = Num(row,   "Hanger Size (InsulationPlusPipeOD)", "Hanger Size", "InsulationPlusPipeOD", "Hanger OD", "OD"),
        };

        // First non-empty text value among the candidate headers (in order).
        private static string First(RawRow row, params string[] columns)
        {
            foreach (string c in columns) { string v = row.Get(c); if (v.Length > 0) return v; }
            return "";
        }

        // First parseable number among the candidate headers (0 if none parse).
        private static double Num(RawRow row, params string[] columns)
        {
            foreach (string c in columns)
            {
                string v = row.Get(c);
                if (v.Length > 0 && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
            }
            return 0;
        }

        // Spacing / setback columns — ASSUMED to be in feet in the schedule, returned in inches (the row's unit).
        private const double FeetToInches = 12.0;
        private static double FeetCol(RawRow row, params string[] columns) => Num(row, columns) * FeetToInches;

        // ── Export: SupportSpecs → Harris hanger-schedule CSV ──────────────────────────────────────────
        // Uses the SAME canonical headers ParseRow consumes, so Export → edit in Excel → Import round-trips
        // cleanly. Setbacks/spacing are written in FEET (the schedule's unit; ParseRow multiplies back ×12);
        // Pipe Size / Hanger OD / Rod Diameter are inches (written as-is). One CSV row per (spec × size band).
        private static readonly string[] CsvHeaders =
        {
            "Service Name", "Media", "Pipe Material Description", "Pipe Insulation Type",
            "Pipe Size", "Hanger Size (InsulationPlusPipeOD)", "Hanger Spacing",
            "Distance From Fitting", "Distance From Joint", "Distance From Anchor",
            "Hanger Type", "Insulation Insert Type", "Rod Diameter",
        };

        /// <summary>Write the given specs to a Harris hanger-schedule CSV at <paramref name="path"/>.</summary>
        public static void WriteCsv(string path, IEnumerable<SupportSpec> specs)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", CsvHeaders));
            foreach (SupportSpec s in specs ?? Enumerable.Empty<SupportSpec>())
            {
                foreach (SupportSpecRow r in s.Rows ?? new List<SupportSpecRow>())
                {
                    string[] cells =
                    {
                        s.Name, s.Media, s.Material, s.Insulation,
                        Inv(r.MaxSizeInches),
                        Inv(r.HangerSizeOdInches),
                        Inv(r.StraightSpacingInches   / FeetToInches),
                        Inv(r.FittingDistanceInches   / FeetToInches),
                        Inv(r.DistanceFromJointInches / FeetToInches),
                        Inv(r.DistanceFromAnchorInches / FeetToInches),
                        r.HangerType,
                        r.InsulationInsertType,
                        Inv(r.RodDiameterInches),
                    };
                    sb.AppendLine(string.Join(",", cells.Select(CsvEscape)));
                }
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static string Inv(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        // Quote a CSV field if it contains a comma, quote, or newline; double embedded quotes.
        private static string CsvEscape(string field)
        {
            field ??= "";
            if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
    }
}
