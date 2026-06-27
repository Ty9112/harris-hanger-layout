using System;
using System.Collections.Generic;

namespace HangerLayout.Models
{
    public enum HangerDomain
    {
        Pipe = 0,
        Duct = 1
    }

    public enum HangerSelectionMode
    {
        CurrentSelection = 0,
        PickElements     = 1,
        AllService       = 2
    }

    public enum HangerServiceScope
    {
        WholeProject = 0,
        ActiveView   = 1
    }

    public enum SupportPositionMode
    {
        NotAtChange          = 0,
        BeforeChange         = 1,
        AfterChange          = 2,
        BeforeAndAfterChange = 3
    }

    public enum StraightJointMode
    {
        NotAtJoint          = 0,
        BeforeJoint         = 1,
        AfterJoint          = 2,
        BeforeAndAfterJoint = 3
    }

    /// <summary>Shape filter for Duct specs. Pipe specs ignore this — they
    /// always use Any. Default is Any so existing specs in saved projects
    /// keep working with both round and rectangular ducts.</summary>
    public enum DuctShape
    {
        Any         = 0,
        Round       = 1,
        Rectangular = 2,
    }

    public class SupportSpecRow
    {
        public double MaxSizeInches           { get; set; }
        public double StraightSpacingInches   { get; set; }
        public double FittingDistanceInches   { get; set; }
        public double DistanceFromJointInches { get; set; }

        // ── Rich Harris-schedule columns (added for the table importer) ────────────────────────────────
        // Defaulted so specs saved by the original add-in (which lack these) still deserialize cleanly — they
        // are serialized as JSON properties, so a missing value just becomes the default (System.Text.Json).
        /// <summary>Hanger component type for this size band, e.g. "Clevis" / "Loop" — resolved to a hanger
        /// button at placement time. Empty = fall back to the spec's HangerOverride / auto-pick.</summary>
        public string HangerType { get; set; } = "";
        /// <summary>Threaded-rod diameter (inches) for this size band, e.g. 0.375. 0 = unspecified.</summary>
        public double RodDiameterInches { get; set; }
    }

    public class SupportSpec
    {
        public Guid                Id             { get; set; } = Guid.NewGuid();
        public string              Name           { get; set; } = string.Empty;
        public HangerDomain        Domain         { get; set; } = HangerDomain.Pipe;

        /// <summary>"GroupName|ButtonName" identifying the hanger button to use, or null
        /// to fall back to the first non-excluded hanger in the part's service.</summary>
        public string?             HangerOverride { get; set; }

        public SupportPositionMode SupportPositions { get; set; } = SupportPositionMode.BeforeAndAfterChange;
        public StraightJointMode   StraightJoints   { get; set; } = StraightJointMode.NotAtJoint;

        /// <summary>Shape filter for Duct specs only. Pipe specs ignore this.
        /// Defaults to Any for backwards compatibility with pre-shape-split
        /// projects — those specs continue to apply to both round and rect
        /// ducts until the user explicitly tags them.</summary>
        public DuctShape DuctShape { get; set; } = DuctShape.Any;

        // ── Rich Harris-schedule match attributes (added for the table importer) ───────────────────────
        // A Harris hanger schedule row is keyed by Service + Media + Material + Insulation + Size. Name holds
        // the Service; these hold the rest. Empty = "matches any" (back-compat with pre-import specs).
        /// <summary>Media / system the spec applies to, e.g. "Domestic Hot Water", "Drainage".</summary>
        public string Media      { get; set; } = "";
        /// <summary>Pipe material, e.g. "Copper", "Cast Iron No Hub", "Stainless STG".</summary>
        public string Material   { get; set; } = "";
        /// <summary>Pipe insulation type, e.g. "Bare Pipe" or the insulation spec.</summary>
        public string Insulation { get; set; } = "";

        public List<SupportSpecRow> Rows          { get; set; } = new();
    }
}
