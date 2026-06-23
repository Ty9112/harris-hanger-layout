using HangerLayout.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Reads Autodesk Fabrication's HSpecs.MAP — the Hanger Specifications
    /// catalog under ESTmep's Database → Specifications → Hangers tab. Each
    /// spec is a named, domain-scoped (PIPEWORK / DUCTWORK / ELECTRICAL /
    /// PUBLIC HEALTH) list of rules; each rule binds a size band to a hanger
    /// component name plus the spacing, fitting-distance, and joint-distance
    /// values that the Hanger Layout tool's SupportSpec already models.
    ///
    /// Format reverse-engineered from a controlled test: spec "ZZ_FINDME"
    /// with values MaxSize=7.5", Spacing=73", FittingDist=17", JointDist=11"
    /// each appeared as the expected IEEE-754 little-endian double, tagged
    /// by a 16-bit field code. See the comments inside ParseRule for the
    /// confirmed code mappings.
    /// </summary>
    public sealed class HSpecsMapReader
    {
        // ── Markers ────────────────────────────────────────────────────────
        private static readonly byte[] CONTAINER_MARKER = { 0xAB, 0xBF, 0x4C, 0x00 };
        private static readonly byte[] RECORD_MARKER    = { 0xAA, 0xBF, 0x4C, 0x00 };
        private static readonly byte[] RULE_MARKER      = { 0xAF, 0xBF, 0x4C, 0x00 };
        private static readonly byte[] AD_MARKER        = { 0xAD, 0xBF, 0x4C, 0x00 };
        private static readonly byte[] AE_MARKER        = { 0xAE, 0xBF, 0x4C, 0x00 };
        private static readonly byte[] AUDIT_MARKER     = { 0xE9, 0xDF, 0xDF, 0x2F };

        // ── Field codes (low 16 bits of the 4-byte tag in each entry's footer) ─
        // Confirmed via ZZ_FINDME test:
        //   MaxSize 7.5  →  0x401E000000000000  appeared with tag 0x03E9 (1001) ✓
        //   Spacing 73   →  0x4052400000000000  appeared with tag 0x0BC7 (3015) ✓
        //   FittingDist 17 → 0x4031000000000000  appeared with tag 0x0BCD (3021) ✓
        //   JointDist 11 →  0x4026000000000000  appeared with tag 0x0BCF (3023) ✓
        // The component-name AE entries (e.g. "Hanger", "RectangularBearer")
        // carry tag 0x0BCE (3022). The min-size AD entry uses 0x03E8 (1000)
        // and the third constraint (always 0 in observed data) uses 0x03EB
        // (1003) — we leave both alone since SupportSpec only models max.
        private const ushort F_MIN_SIZE      = 0x03E8;
        private const ushort F_MAX_SIZE      = 0x03E9;
        private const ushort F_RESERVED_1003 = 0x03EB;
        private const ushort F_SPACING       = 0x0BC7;
        private const ushort F_FITTING_DIST  = 0x0BCD;
        private const ushort F_COMPONENT     = 0x0BCE;
        private const ushort F_JOINT_DIST    = 0x0BCF;

        public sealed class HSpec
        {
            public string Name = "";
            public string Domain = "";
            public List<HSpecRule> Rules = new();
        }

        public sealed class HSpecRule
        {
            public double MinSizeInches;
            public double MaxSizeInches;
            public double StraightSpacingInches;
            public double FittingDistanceInches;
            public double DistanceFromJointInches;
            public string ComponentName = "";
        }

        /// <summary>
        /// Parse the raw bytes of HSpecs.MAP (zlib-wrapped). Returns the spec
        /// catalog or an empty list if the file isn't recognisable.
        /// </summary>
        public static List<HSpec> Read(string filePath)
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(filePath); }
            catch { return new(); }

            byte[]? payload = MapFileHelper.TryDecompress(raw, out _, out _);
            if (payload == null) return new();

            var result = new List<HSpec>();
            int containerPos = FindMarker(payload, 0, CONTAINER_MARKER);
            if (containerPos < 0) return result;

            // Container layout:
            //   [AB BF 4C 00] [uint32 size] [uint32 payload_size]
            //   [uint32 record_count] [4 bytes padding] [records...]
            int containerSize = BitConverter.ToInt32(payload, containerPos + 4);
            int containerEnd  = Math.Min(payload.Length, containerPos + 12 + containerSize);
            int pos = containerPos + 20;

            while (pos < containerEnd - 4)
            {
                int recStart = FindMarker(payload, pos, RECORD_MARKER);
                if (recStart < 0 || recStart >= containerEnd) break;
                var spec = ParseRecord(payload, recStart, containerEnd, out int next);
                if (spec != null) result.Add(spec);
                pos = next;
            }
            return result;
        }

        // ── Record parsing ─────────────────────────────────────────────────

        private static HSpec? ParseRecord(byte[] data, int start, int containerEnd, out int next)
        {
            // Record layout:
            //   [AA BF 4C 00] [uint32 size] [uint32 payload_size]
            //   [uint32 unknown_1 = 0x08]
            //   [uint32 name_len_chars] [UTF-16 name including null]
            //   [16 bytes header_2]
            //   [uint32 domain_len_chars] [UTF-16 domain including null]
            //   [24 bytes header_3] (or variable; we scan for the audit marker)
            //   [E9 DF DF 2F] [uint32 audit_size] [audit_size bytes audit]
            //   [variable bytes] [AF BF rules ...]
            int p = start + 4;
            int size = BitConverter.ToInt32(data, p); p += 4;
            int recordEnd = Math.Min(containerEnd, start + 12 + size);
            next = recordEnd;

            p += 4;  // payload_size (not needed; we use size + 12)
            p += 4;  // unknown_1

            int nameLen = SafeInt32(data, p); p += 4;
            string name = ReadUtf16(data, p, nameLen);
            p += nameLen * 2;

            p += 16;  // header_2 — observed constant

            int domainLen = SafeInt32(data, p); p += 4;
            string domain = ReadUtf16(data, p, domainLen);
            p += domainLen * 2;

            // Skip ahead to the audit marker — header_3 length varies slightly
            // between specs, but the audit marker is unique enough to scan for.
            int auditPos = FindMarker(data, p, AUDIT_MARKER);
            if (auditPos < 0 || auditPos >= recordEnd) return null;
            p = auditPos + 4;
            int auditSize = SafeInt32(data, p); p += 4;
            p += auditSize;

            var spec = new HSpec { Name = name, Domain = domain };
            while (p < recordEnd - 4)
            {
                int rulePos = FindMarker(data, p, RULE_MARKER);
                if (rulePos < 0 || rulePos >= recordEnd) break;
                var rule = ParseRule(data, rulePos, recordEnd, out int ruleNext);
                if (rule != null) spec.Rules.Add(rule);
                p = ruleNext;
            }
            return spec;
        }

        // ── Rule parsing ───────────────────────────────────────────────────

        private static HSpecRule? ParseRule(byte[] data, int start, int recordEnd, out int next)
        {
            // Rule layout:
            //   [AF BF 4C 00] [uint32 size] [uint32 payload_size_inner]
            //   [uint32 ad_count] [4 bytes padding] [ad entries] [ae entries]
            int p = start + 4;
            int size = BitConverter.ToInt32(data, p); p += 4;
            int ruleEnd = Math.Min(recordEnd, start + 12 + size);
            next = ruleEnd;

            p += 4;  // payload_size_inner
            int adCount = SafeInt32(data, p); p += 4;
            p += 4;  // padding

            var rule = new HSpecRule();

            // ── AD entries (size-band constraints) ────────────────────────
            // Each: [marker 4][size=0x18 4][payload_size=0x0C 4]
            //       [uint32 idx][double value]      ← 12-byte payload
            //       [04 00 00 00][uint16 fieldCode][2 0][4 0]  ← 12-byte footer
            // …plus an extra 4-byte block on the 1003-coded entry.
            for (int i = 0; i < adCount; i++)
            {
                int adPos = FindMarker(data, p, AD_MARKER);
                if (adPos < 0 || adPos >= ruleEnd) break;
                p = adPos + 4;
                p += 4;  // size
                p += 4;  // payload_size
                p += 4;  // idx
                double value = SafeDouble(data, p); p += 8;
                // Footer
                p += 4;  // 04 00 00 00
                ushort fieldCode = SafeUInt16(data, p);
                p += 4;  // 2-byte code + 2 zeros
                p += 4;  // 4 zeros
                if (fieldCode == F_RESERVED_1003) p += 4;  // extra block on third AD

                if      (fieldCode == F_MIN_SIZE) rule.MinSizeInches = value;
                else if (fieldCode == F_MAX_SIZE) rule.MaxSizeInches = value;
            }

            // ── AE entries (component + spacing / setbacks) ───────────────
            // Component AE: payload is [8 reserved][uint32 strlen]
            //                          [UTF-16 chars with null]
            // Numeric AE:   payload is [double value][uint32=1][2 trailing]
            //               (value sits at offset 0 within the payload, NOT
            //                offset 4 — earlier confusion came from miscounting
            //                the previous entry's footer as this entry's prefix.)
            // Footer (both): [04 00 00 00][uint16 fieldCode][2 0][4 0]
            while (p < ruleEnd - 4)
            {
                int aePos = FindMarker(data, p, AE_MARKER);
                if (aePos < 0 || aePos >= ruleEnd) break;
                p = aePos + 4;
                p += 4;  // size
                int payloadSize = SafeInt32(data, p); p += 4;

                int payloadStart = p;
                p += payloadSize;

                p += 4;  // 04 00 00 00
                ushort fieldCode = SafeUInt16(data, p);
                p += 4;
                p += 4;

                switch (fieldCode)
                {
                    case F_COMPONENT:
                        // Resolve the component name from the string payload
                        if (payloadSize >= 12)
                        {
                            int strLen = SafeInt32(data, payloadStart + 8);
                            rule.ComponentName = ReadUtf16(data, payloadStart + 12, strLen);
                        }
                        break;
                    case F_SPACING:
                        rule.StraightSpacingInches = SafeDouble(data, payloadStart);
                        break;
                    case F_FITTING_DIST:
                        rule.FittingDistanceInches = SafeDouble(data, payloadStart);
                        break;
                    case F_JOINT_DIST:
                        rule.DistanceFromJointInches = SafeDouble(data, payloadStart);
                        break;
                }
            }
            return rule;
        }

        // ── Conversion to the dialog's SupportSpec model ───────────────────

        /// <summary>
        /// Convert the raw HSpec records into <see cref="SupportSpec"/>
        /// instances ready to merge into the Hanger Layout dialog. Pipe and
        /// Duct domains map directly; ELECTRICAL / PUBLIC HEALTH and other
        /// unknown domains are skipped (the dialog currently only models
        /// Pipework + Ductwork).
        ///
        /// Duct records SPLIT by component-name shape hint so the user gets
        /// distinct Round and Rectangular specs to drop into the two duct
        /// slots — rather than one Any-tagged spec that duplicates into both
        /// dropdowns. ESTmep encodes the shape via the per-rule component
        /// name: "RectangularBearer" rules belong to rectangular ducts;
        /// everything else (typically just "Hanger") goes to round.
        /// </summary>
        public static List<SupportSpec> ToSupportSpecs(List<HSpec> hspecs)
        {
            var result = new List<SupportSpec>();
            foreach (var h in hspecs)
            {
                HangerDomain? domain = h.Domain.ToUpperInvariant() switch
                {
                    "PIPEWORK" => HangerDomain.Pipe,
                    "DUCTWORK" => HangerDomain.Duct,
                    _          => null,
                };
                if (domain == null) continue;

                if (domain == HangerDomain.Pipe)
                {
                    // Pipe domain has no shape split — one spec per record.
                    var pipe = BuildSpec(h.Name, HangerDomain.Pipe, DuctShape.Any, h.Rules);
                    if (pipe != null) result.Add(pipe);
                    continue;
                }

                // Duct domain: bucket rules by component-name shape hint.
                var roundRules = new List<HSpecRule>();
                var rectRules  = new List<HSpecRule>();
                foreach (var r in h.Rules)
                {
                    bool isRect = r.ComponentName != null &&
                                  r.ComponentName.IndexOf("Rectangular",
                                      StringComparison.OrdinalIgnoreCase) >= 0;
                    (isRect ? rectRules : roundRules).Add(r);
                }

                // Single-bucket records: emit one spec tagged with the
                // appropriate shape (no name suffix needed).
                if (rectRules.Count == 0)
                {
                    var only = BuildSpec(h.Name, HangerDomain.Duct, DuctShape.Round, roundRules);
                    if (only != null) result.Add(only);
                }
                else if (roundRules.Count == 0)
                {
                    var only = BuildSpec(h.Name, HangerDomain.Duct, DuctShape.Rectangular, rectRules);
                    if (only != null) result.Add(only);
                }
                else
                {
                    // Mixed: emit one spec per shape with disambiguating
                    // suffix on the name so the user sees both in the spec
                    // list.
                    var round = BuildSpec($"{h.Name} (Round)", HangerDomain.Duct, DuctShape.Round, roundRules);
                    var rect  = BuildSpec($"{h.Name} (Rect)",  HangerDomain.Duct, DuctShape.Rectangular, rectRules);
                    if (round != null) result.Add(round);
                    if (rect  != null) result.Add(rect);
                }
            }
            return result;
        }

        private static SupportSpec? BuildSpec(
            string name, HangerDomain domain, DuctShape shape, List<HSpecRule> rules)
        {
            var spec = new SupportSpec
            {
                Id        = Guid.NewGuid(),
                Name      = name,
                Domain    = domain,
                DuctShape = shape,
            };
            foreach (var r in rules)
            {
                // Skip rules with no usable max size — those are placeholder
                // entries (e.g. the "default" line where Fab encodes "any").
                if (r.MaxSizeInches <= 0) continue;
                spec.Rows.Add(new SupportSpecRow
                {
                    MaxSizeInches           = r.MaxSizeInches,
                    StraightSpacingInches   = r.StraightSpacingInches,
                    FittingDistanceInches   = r.FittingDistanceInches,
                    DistanceFromJointInches = r.DistanceFromJointInches,
                });
            }
            return spec.Rows.Count > 0 ? spec : null;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static int FindMarker(byte[] data, int start, byte[] marker)
        {
            int max = data.Length - marker.Length;
            for (int i = start; i <= max; i++)
            {
                bool ok = true;
                for (int j = 0; j < marker.Length; j++)
                {
                    if (data[i + j] != marker[j]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
        }

        private static string ReadUtf16(byte[] data, int offset, int chars)
        {
            if (chars <= 0 || offset < 0 || offset + chars * 2 > data.Length) return "";
            string s = Encoding.Unicode.GetString(data, offset, chars * 2);
            int nullIdx = s.IndexOf('\0');
            return nullIdx >= 0 ? s.Substring(0, nullIdx) : s;
        }

        private static int    SafeInt32(byte[] d, int p) =>
            (p < 0 || p + 4 > d.Length) ? 0 : BitConverter.ToInt32(d, p);
        private static ushort SafeUInt16(byte[] d, int p) =>
            (p < 0 || p + 2 > d.Length) ? (ushort)0 : BitConverter.ToUInt16(d, p);
        private static double SafeDouble(byte[] d, int p) =>
            (p < 0 || p + 8 > d.Length) ? 0.0 : BitConverter.ToDouble(d, p);
    }
}
