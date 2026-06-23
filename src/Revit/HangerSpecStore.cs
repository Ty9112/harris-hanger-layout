using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using HangerLayout.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Persists user-defined Support Specifications inside the Revit project via
    /// ExtensibleStorage on ProjectInformation. Mirrors the lifecycle pattern in
    /// ServiceToSpecMapper but stores a richer object graph as JSON.
    /// </summary>
    internal static class HangerSpecStore
    {
        // Fresh schema GUID for the standalone tool — different from the
        // PCF Exporter integration's GUID so the two tools (if both are
        // installed on the same machine) keep their saved specs independent.
        private static readonly Guid SchemaGuid =
            new("8C3F2B4E-9D4F-4C9B-B67E-3D5F92DA014F");
        private const string SchemaName = "HangerLayout_HangerSpecs";
        private const string FieldName  = "HangerSpecsJson";

        private static readonly JsonSerializerOptions s_jsonOpts = new()
        {
            WriteIndented = false,
            IncludeFields = false
        };

        public static List<SupportSpec> Load(Document doc)
        {
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return new List<SupportSpec>();

                var entity = doc.ProjectInformation.GetEntity(schema);
                if (!entity.IsValid()) return new List<SupportSpec>();

                string json = entity.Get<string>(schema.GetField(FieldName));
                if (string.IsNullOrWhiteSpace(json)) return new List<SupportSpec>();

                var loaded = JsonSerializer.Deserialize<List<SupportSpec>>(json, s_jsonOpts);
                return loaded ?? new List<SupportSpec>();
            }
            catch
            {
                return new List<SupportSpec>();
            }
        }

        /// <summary>Caller must wrap in a Transaction.</summary>
        public static void Save(Document doc, List<SupportSpec> specs)
        {
            var schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            string json = JsonSerializer.Serialize(specs ?? new List<SupportSpec>(), s_jsonOpts);
            entity.Set(schema.GetField(FieldName), json);
            doc.ProjectInformation.SetEntity(entity);
        }

        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }
    }
}
