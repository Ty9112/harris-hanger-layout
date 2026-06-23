using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Tiny per-project settings store on <c>doc.ProjectInformation</c>.
    /// Currently holds one field: the path to the Fabrication
    /// <c>Database</c> folder, used to auto-locate <c>HSpecs.MAP</c> for
    /// the Import from Fabrication Config feature. Persisted via
    /// ExtensibleStorage so it survives save/close cycles.
    ///
    /// Reads return <c>null</c> when no value is set yet, which is the
    /// caller's signal to prompt the user to browse for the folder.
    /// </summary>
    internal static class HangerSettingsStore
    {
        // Fresh GUID specifically for this settings schema. Not shared
        // with HangerSpecStore — they're independent.
        private static readonly Guid SchemaGuid =
            new("4A8B2C9D-5E6F-4B1A-9D3C-7F2E81AB6543");
        private const string SchemaName = "HangerLayout_Settings";
        private const string FieldFabDbFolder = "FabricationDatabaseFolder";

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
    }
}
