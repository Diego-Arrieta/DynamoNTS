using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
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
        public NtsSchema Schema { get; private set; }

        /// <summary>
        /// List of all column names registered in this record.
        /// </summary>
        public List<string> Keys => InternalTable.GetNames().ToList();

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
                table.Add(attr.Name, attr.Value);
                detectedFields.Add(attr.Field);
            }

            var finalSchema = schema ?? NtsSchema.ByFields(detectedFields);

            // Ensure all schema fields exist in the table (populate missing ones with null)
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
                object? val = InternalTable.Exists(field.Name) ? InternalTable[field.Name] : null;
                list.Add(new NtsAttribute(field, val is DBNull ? null : val));
            }
            return list;
        }

        /// <summary>
        /// Retrieves an individual atomic NtsAttribute cell by field name or NtsField definition.
        /// </summary>
        /// <param name="field">Column name (string) or NtsField definition.</param>
        /// <returns name="attribute">The corresponding NtsAttribute cell, or null if not found.</returns>
        public NtsAttribute? GetAttribute(object field)
        {
            string? fieldName = ExtractFieldName(field);
            if (string.IsNullOrWhiteSpace(fieldName) || !InternalTable.Exists(fieldName))
                return null;

            var val = InternalTable[fieldName];
            var f = Schema.FieldByName(fieldName) ?? (field as NtsField) ?? NtsField.ByNameAndType(fieldName, NtsAttribute.InferType(val));

            return new NtsAttribute(f, val is DBNull ? null : val);
        }

        /// <summary>
        /// Inserts or updates an individual atomic NtsAttribute cell in this record.
        /// </summary>
        /// <param name="attribute">The attribute cell to set.</param>
        /// <returns name="record">The updated NtsRecord.</returns>
        public NtsRecord SetAttribute(NtsAttribute attribute)
        {
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));

            InternalTable[attribute.Name] = attribute.Value;

            if (Schema.FieldByName(attribute.Name) == null)
            {
                Schema = Schema.AddField(attribute.Field);
            }

            return this;
        }

        /// <summary>
        /// Gets the raw attribute value by column name or NtsField definition.
        /// </summary>
        /// <param name="field">Column name (string) or NtsField definition.</param>
        /// <returns name="value">The attribute value, or null if not set.</returns>
        public object? GetValue(object field)
        {
            string? name = ExtractFieldName(field);
            if (string.IsNullOrWhiteSpace(name) || !InternalTable.Exists(name)) return null;

            var val = InternalTable[name];
            return val is DBNull ? null : val;
        }

        /// <summary>
        /// Sets or updates a raw attribute value by column name or NtsField definition.
        /// </summary>
        /// <param name="field">Column name (string) or NtsField definition.</param>
        /// <param name="value">The value to set.</param>
        /// <returns name="record">The updated NtsRecord.</returns>
        public NtsRecord SetValue(object field, object? value)
        {
            string? name = ExtractFieldName(field);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Invalid field identifier.");

            var schemaField = Schema.FieldByName(name) ?? (field as NtsField) ?? NtsField.ByNameAndType(name, NtsAttribute.InferType(value));
            object? coerced = NtsAttribute.CoerceValue(value, schemaField);

            InternalTable[name] = coerced;

            if (Schema.FieldByName(name) == null)
            {
                Schema = Schema.AddField(schemaField);
            }

            return this;
        }

        /// <summary>
        /// Converts the record into a native Dynamo dictionary.
        /// </summary>
        /// <returns name="dictionary">A Dynamo dictionary.</returns>
        public Dictionary<string, object?> ToDictionary()
        {
            var dict = new Dictionary<string, object?>();
            foreach (var field in Schema.Fields)
            {
                dict[field.Name] = GetValue(field.Name);
            }
            return dict;
        }

        #endregion

        #region Helpers

        private static string? ExtractFieldName(object field)
        {
            if (field is string s) return s;
            if (field is NtsField f) return f.Name;
            return null;
        }

        #endregion

        #region Indexers

        /// <summary>
        /// Gets or sets an attribute value by field column name using square-bracket indexing.
        /// Example: record["DIAMETRO"] = 250;
        /// </summary>
        /// <param name="fieldName">Field column name.</param>
        /// <returns>The attribute value, or null if not found.</returns>
        public object? this[string fieldName]
        {
            get => GetValue(fieldName);
            set => SetValue(fieldName, value);
        }

        /// <summary>
        /// Gets or sets an attribute value by formal NtsField definition using square-bracket indexing.
        /// </summary>
        /// <param name="field">The NtsField instance.</param>
        /// <returns>The attribute value, or null if not found.</returns>
        public object? this[NtsField field]
        {
            get => GetValue(field);
            set => SetValue(field, value);
        }

        #endregion
        public override string ToString()
        {
            return $"NtsRecord({Count} Attributes: {string.Join(", ", Keys)})";
        }
    }
}