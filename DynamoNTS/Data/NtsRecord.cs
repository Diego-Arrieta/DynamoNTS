using Autodesk.DesignScript.Runtime;
using NetTopologySuite.Features;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamoNTS.Data
{
    /// <summary>
    /// Represents the complete tabular record (row) of attributes associated with a spatial feature.
    /// </summary>
    public class NtsRecord
    {
        [IsVisibleInDynamoLibrary(false)]
        internal AttributesTable InternalTable { get; }

        /// <summary>
        /// The formal attribute schema governing this record.
        /// </summary>
        public NtsSchema Schema { get; }

        /// <summary>
        /// List of all column names registered in this record.
        /// </summary>
        public List<string> Keys => Schema.FieldCount > 0 ? Schema.FieldNames : InternalTable.GetNames().ToList();

        /// <summary>
        /// Total number of attribute columns in this record.
        /// </summary>
        public int Count => InternalTable.Count;

        /// <summary>
        /// List of all NtsField definitions present in this record.
        /// </summary>
        public List<NtsField> Fields => Schema.Fields;

        /// <summary>
        /// List of all stored values ordered according to the schema.
        /// </summary>
        public List<object?> Values
        {
            get
            {
                return Schema.Fields
                    .Select(f => GetValue(f.Name))
                    .ToList();
            }
        }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsRecord(AttributesTable? table = null, NtsSchema? schema = null)
        {
            InternalTable = table ?? new AttributesTable();
            Schema = schema ?? NtsSchema.Empty();
        }

        #region Create Nodes

        /// <summary>
        /// Constructs a record from a collection of atomic NtsAttribute instances, optionally enforcing a formal schema.
        /// </summary>
        /// <param name="attributes">Collection of NtsAttribute instances.</param>
        /// <param name="schema">Optional formal schema definition to enforce and populate.</param>
        /// <returns name="record">The constructed NtsRecord instance.</returns>
        public static NtsRecord ByAttributes(
            IList<NtsAttribute> attributes,
            [DefaultArgument("null")] NtsSchema? schema = null)
        {
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));

            var table = new AttributesTable();
            var detectedFields = new List<NtsField>();

            foreach (var attr in attributes)
            {
                if (attr == null) continue;
                string canonicalName = schema?.FieldByName(attr.Name)?.Name ?? attr.Name;
                table.Add(canonicalName, attr.Value);
                detectedFields.Add(attr.Field);
            }

            var finalSchema = schema ?? NtsSchema.ByFields(detectedFields);

            foreach (var field in finalSchema.Fields)
            {
                if (!table.Exists(field.Name))
                {
                    table.Add(field.Name, null);
                }
            }

            return new NtsRecord(table, finalSchema);
        }

        /// <summary>
        /// Builds an attribute record from a standard Dynamo key-value dictionary.
        /// </summary>
        /// <param name="dictionary">Dictionary containing attribute key-value pairs.</param>
        /// <returns name="record">The constructed NtsRecord.</returns>
        public static NtsRecord FromDictionary(IDictionary<string, object?> dictionary)
        {
            if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));
            if (dictionary.Count == 0) return NtsRecord.Empty();

            var table = new AttributesTable();
            var fields = new List<NtsField>();

            foreach (var kvp in dictionary)
            {
                var field = NtsField.ByNameAndType(kvp.Key, NtsAttribute.InferType(kvp.Value));
                table.Add(field.Name, NtsAttribute.CoerceValue(kvp.Value, field));
                fields.Add(field);
            }

            return new NtsRecord(table, NtsSchema.ByFields(fields));
        }

        /// <summary>
        /// Initializes a record matching a formal schema structure with all values set to null.
        /// </summary>
        /// <param name="schema">The formal schema template.</param>
        /// <returns name="record">An initialized NtsRecord.</returns>
        public static NtsRecord FromSchema(NtsSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            var table = new AttributesTable();
            foreach (var f in schema.Fields)
            {
                table.Add(f.Name, null);
            }

            return new NtsRecord(table, schema);
        }

        /// <summary>
        /// Creates an empty record without fields.
        /// </summary>
        /// <returns name="record">An empty NtsRecord.</returns>
        public static NtsRecord Empty()
        {
            return new NtsRecord(new AttributesTable(), NtsSchema.Empty());
        }

        #endregion

        #region Key Resolution

        /// <summary>
        /// Resolves a field identifier to its canonical schema or table key (case-insensitive).
        /// </summary>
        /// <param name="name">The raw field name.</param>
        /// <returns>The canonical key name.</returns>
        [IsVisibleInDynamoLibrary(false)]
        public string ResolveKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;

            // 1. Prioridad: Esquema formal registrado
            var schemaField = Schema?.FieldByName(name);
            if (schemaField != null)
                return schemaField.Name;

            // 2. Prioridad: Clave existente en la tabla interna
            if (InternalTable != null)
            {
                if (InternalTable.Exists(name))
                    return name;

                foreach (var existingName in InternalTable.GetNames())
                {
                    if (string.Equals(existingName, name, StringComparison.OrdinalIgnoreCase))
                        return existingName;
                }
            }

            return name.Trim();
        }

        private string? FindTableKey(string canonicalKey)
        {
            if (InternalTable.Exists(canonicalKey))
                return canonicalKey;

            foreach (var existing in InternalTable.GetNames())
            {
                if (string.Equals(existing, canonicalKey, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            return null;
        }

        #endregion
        
        #region Action / Query Nodes

        /// <summary>
        /// Extracts all atomic NtsAttribute cells from this record as an ordered list.
        /// </summary>
        /// <returns name="attributes">List of individual NtsAttribute cells.</returns>
        public List<NtsAttribute> GetAttributes()
        {
            var list = new List<NtsAttribute>();
            foreach (var field in Schema.Fields)
            {
                object? val = GetValue(field.Name);
                list.Add(new NtsAttribute(field, val is DBNull ? null : val));
            }
            return list;
        }

        /// <summary>
        /// Retrieves an individual atomic NtsAttribute cell by column name or NtsField definition.
        /// </summary>
        /// <param name="field">Column name (string) or formal NtsField definition.</param>
        /// <returns name="attribute">The corresponding NtsAttribute cell, or null if not found.</returns>
        public NtsAttribute? GetAttribute(object field)
        {
            string? rawName = ExtractFieldName(field);
            if (string.IsNullOrWhiteSpace(rawName)) return null;

            string canonicalKey = ResolveKey(rawName);
            string? tableKey = FindTableKey(canonicalKey);
            if (tableKey == null) return null;

            var val = InternalTable[tableKey];
            var f = Schema.FieldByName(canonicalKey) ?? (field as NtsField) ?? NtsField.ByNameAndType(canonicalKey, NtsAttribute.InferType(val));

            return new NtsAttribute(f, val is DBNull ? null : val);
        }

        /// <summary>
        /// Retrieves an individual atomic NtsAttribute cell by column name.
        /// </summary>
        /// <param name="fieldName">Field column name.</param>
        /// <returns name="attribute">The corresponding NtsAttribute cell, or null if not found.</returns>
        public NtsAttribute? GetAttributeByName(string fieldName) => GetAttribute(fieldName);

        /// <summary>
        /// Gets the raw attribute value by column name or NtsField definition using canonical key resolution.
        /// </summary>
        /// <param name="field">Column name (string) or formal NtsField definition.</param>
        /// <returns name="value">The attribute value, or null if not set.</returns>
        public object? GetValue(object field)
        {
            string? rawName = ExtractFieldName(field);
            if (string.IsNullOrWhiteSpace(rawName)) return null;

            string canonicalKey = ResolveKey(rawName);
            string? tableKey = FindTableKey(canonicalKey);
            if (tableKey == null) return null;

            var val = InternalTable[tableKey];
            return val is DBNull ? null : val;
        }

        /// <summary>
        /// Gets an attribute value directly by column name.
        /// </summary>
        /// <param name="fieldName">Field column name.</param>
        /// <returns name="value">The attribute value, or null if not set.</returns>
        public object? GetValueByName(string fieldName) => GetValue(fieldName);

        /// <summary>
        /// Creates a new NtsRecord instance with the specified attribute added or updated.
        /// </summary>
        /// <param name="attribute">The attribute cell to set.</param>
        /// <returns name="record">A new NtsRecord instance with the update.</returns>
        public NtsRecord WithAttribute(NtsAttribute attribute)
        {
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));

            var newTable = new AttributesTable();
            foreach (var key in InternalTable.GetNames())
            {
                newTable.Add(key, InternalTable[key]);
            }

            string canonicalKey = ResolveKey(attribute.Name);
            string? existingTableKey = FindTableKey(canonicalKey);
            if (existingTableKey != null && !string.Equals(existingTableKey, canonicalKey, StringComparison.Ordinal))
            {
                newTable.DeleteAttribute(existingTableKey);
            }

            if (newTable.Exists(canonicalKey))
                newTable[canonicalKey] = attribute.Value;
            else
                newTable.Add(canonicalKey, attribute.Value);

            var newSchema = Schema;
            if (newSchema.FieldByName(canonicalKey) == null)
            {
                newSchema = newSchema.AddField(attribute.Field);
            }

            return new NtsRecord(newTable, newSchema);
        }

        /// <summary>
        /// Creates a new NtsRecord instance with the specified attribute value added or updated.
        /// </summary>
        /// <param name="field">Column name (string) or formal NtsField definition.</param>
        /// <param name="value">The value to set.</param>
        /// <returns name="record">A new NtsRecord instance with the update.</returns>
        public NtsRecord WithValue(object field, object? value)
        {
            string? rawName = ExtractFieldName(field);
            if (string.IsNullOrWhiteSpace(rawName))
                throw new ArgumentException("Invalid field identifier.");

            string canonicalKey = ResolveKey(rawName);

            var schemaField = Schema.FieldByName(canonicalKey)
                              ?? (field as NtsField)
                              ?? NtsField.ByNameAndType(canonicalKey, NtsAttribute.InferType(value));

            object? coerced = NtsAttribute.CoerceValue(value, schemaField);

            var newTable = new AttributesTable();
            foreach (var key in InternalTable.GetNames())
            {
                newTable.Add(key, InternalTable[key]);
            }

            string? existingTableKey = FindTableKey(canonicalKey);
            if (existingTableKey != null && !string.Equals(existingTableKey, canonicalKey, StringComparison.Ordinal))
            {
                newTable.DeleteAttribute(existingTableKey);
            }

            if (newTable.Exists(canonicalKey))
                newTable[canonicalKey] = coerced;
            else
                newTable.Add(canonicalKey, coerced);

            var newSchema = Schema;
            if (newSchema.FieldByName(canonicalKey) == null)
            {
                newSchema = newSchema.AddField(schemaField);
            }

            return new NtsRecord(newTable, newSchema);
        }

        /// <summary>
        /// Creates a new NtsRecord instance with the specified attribute value updated by column name.
        /// </summary>
        /// <param name="fieldName">Field column name.</param>
        /// <param name="value">The value to set.</param>
        /// <returns name="record">A new NtsRecord instance with the update.</returns>
        public NtsRecord WithValueByName(string fieldName, object? value) => WithValue(fieldName, value);

        #endregion

        #region Helpers

        private static string? ExtractFieldName(object field)
        {
            if (field is string s) return s;
            if (field is NtsField f) return f.Name;
            return null;
        }
        internal NtsRecord Clone()
        {
            var newTable = new AttributesTable();
            foreach (var key in InternalTable.GetNames())
            {
                newTable.Add(key, InternalTable[key]);
            }

            return new NtsRecord(newTable, Schema);
        }

        #endregion

        [IsVisibleInDynamoLibrary(false)]
        public override string ToString()
        {
            return $"NtsRecord({Count} Attributes: {string.Join(", ", Keys)})";
        }
    }
}