using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Per-project settings store on <c>doc.ProjectInformation</c>. Holds
    /// two independent groups (each its own ExtensibleStorage schema, so
    /// they can evolve separately):
    /// <list type="bullet">
    ///   <item>Fabrication Database folder hint (used to auto-locate
    ///         <c>HSpecs.MAP</c> for the Import from Fabrication Config feature).</item>
    ///   <item>Placement settings (currently: minimum-spacing skip
    ///         threshold and its enable flag).</item>
    /// </list>
    /// Persisted via ExtensibleStorage so settings survive save/close
    /// cycles. Reads return defaults (or null) when no value is set yet.
    /// </summary>
    internal static class HangerSettingsStore
    {
        // ── Fabrication Database folder hint (Schema A) ────────────────────
        private static readonly Guid SchemaGuid =
            new("4A8B2C9D-5E6F-4B1A-9D3C-7F2E81AB6543");
        private const string SchemaName = "HangerLayout_Settings";
        private const string FieldFabDbFolder = "FabricationDatabaseFolder";

        // ── Placement settings (Schema B) ──────────────────────────────────
        // Separate schema so adding new placement fields later doesn't risk
        // colliding with Schema A in projects where Schema A is already
        // saved with only its original field.
        private static readonly Guid PlacementSchemaGuid =
            new("9D5E1F3A-7C2B-4D8E-A91C-6B4F03DE82B7");
        private const string PlacementSchemaName = "HangerLayout_PlacementSettings";
        private const string FieldMinSpacingEnabled = "MinSpacingEnabled";
        private const string FieldMinSpacingInches  = "MinSpacingInches";

        public sealed class PlacementSettings
        {
            public bool   MinSpacingEnabled { get; set; }
            public double MinSpacingInches  { get; set; } = 6.0;
        }

        public static string? GetFabricationDatabaseFolder(Document doc)
        {
            try
            {
                var entity = LookupEntity(doc);
                if (entity == null || !entity.IsValid()) return null;
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var field = schema.GetField(FieldFabDbFolder);
                if (field == null) return null;
                string value = entity.Get<string>(field) ?? string.Empty;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch { return null; }
        }

        public static void SetFabricationDatabaseFolder(Document doc, string folder)
        {
            try
            {
                var schema = EnsureSchema();
                var entity = new Entity(schema);
                entity.Set(schema.GetField(FieldFabDbFolder), folder ?? string.Empty);
                doc.ProjectInformation.SetEntity(entity);
            }
            catch
            {
                // Silent failure is acceptable here — the auto-locate is a
                // convenience, not a correctness requirement; the caller
                // falls back to a file picker.
            }
        }

        // ── Placement settings (Schema B) ──────────────────────────────────

        /// <summary>Returns the saved placement settings, or fresh defaults
        /// if none have been written yet.</summary>
        public static PlacementSettings GetPlacementSettings(Document doc)
        {
            var defaults = new PlacementSettings();
            try
            {
                var schema = Schema.Lookup(PlacementSchemaGuid);
                if (schema == null) return defaults;
                var entity = doc.ProjectInformation.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return defaults;
                return new PlacementSettings
                {
                    MinSpacingEnabled = entity.Get<bool>(schema.GetField(FieldMinSpacingEnabled)),
                    MinSpacingInches  = entity.Get<double>(schema.GetField(FieldMinSpacingInches)),
                };
            }
            catch { return defaults; }
        }

        public static void SetPlacementSettings(Document doc, PlacementSettings settings)
        {
            if (settings == null) return;
            try
            {
                var schema = EnsurePlacementSchema();
                var entity = new Entity(schema);
                entity.Set(schema.GetField(FieldMinSpacingEnabled), settings.MinSpacingEnabled);
                entity.Set(schema.GetField(FieldMinSpacingInches),  settings.MinSpacingInches);
                doc.ProjectInformation.SetEntity(entity);
            }
            catch
            {
                // Silent — placement settings round-trip is best-effort.
            }
        }

        // ── Internals ──────────────────────────────────────────────────────

        private static Entity? LookupEntity(Document doc)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;
            try { return doc.ProjectInformation.GetEntity(schema); }
            catch { return null; }
        }

        private static Schema EnsureSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldFabDbFolder, typeof(string));
            return builder.Finish();
        }

        private static Schema EnsurePlacementSchema()
        {
            var schema = Schema.Lookup(PlacementSchemaGuid);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(PlacementSchemaGuid);
            builder.SetSchemaName(PlacementSchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldMinSpacingEnabled, typeof(bool));
            builder.AddSimpleField(FieldMinSpacingInches,  typeof(double));
            return builder.Finish();
        }
    }
}
