using Autodesk.DesignScript.Runtime;
using DynamoNTS.Layers;
using NetTopologySuite.Features;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DynamoNTS.Features
{
    /// <summary>
    /// Encapsulates a strongly-typed tabular attribute record for a spatial feature.
    /// </summary>
    public class NtsAttributes
    {
        [IsVisibleInDynamoLibrary(false)]
        internal AttributesTable InternalTable { get; }

        /// <summary>
        /// The associated schema, if defined.
        /// </summary>
        public NtsSchema Schema { get; }

        /// <summary>
        /// Gets all field names stored in this attribute record.
        /// </summary>
        public List<string> Keys => InternalTable.GetNames().ToList();

        /// <summary>
        /// Gets the count of attributes in this record.
        /// </summary>
        public int Count => InternalTable.Count;

        [IsVisibleInDynamoLibrary(false)]
        internal NtsAttributes(AttributesTable table, NtsSchema schema = null)
        {
            InternalTable = table ?? new AttributesTable();
            Schema = schema;
        }

        #region Create Nodes

        /// <summary>
        /// Creates an attribute record matching an NtsSchema from a list of row values.
        /// </summary>
        /// <param name="schema">The table schema.</param>
        /// <param name="values">The row values in the exact order of the schema fields.</param>
        /// <returns name="attributes">The validated NtsAttributes instance.</returns>
        public static NtsAttributes BySchemaAndValues(NtsSchema schema, IList<object> values)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (values == null) throw new ArgumentNullException(nameof(values));

            if (values.Count != schema.FieldCount)
                throw new ArgumentException($"Value count ({values.Count}) does not match schema field count ({schema.FieldCount}).");

            var table = new AttributesTable();
            for (int i = 0; i < schema.FieldCount; i++)
            {
                var field = schema.Fields[i];
                object coercedValue = CoerceValue(values[i], field);
                table.Add(field.Name, coercedValue);
            }

            return new NtsAttributes(table, schema);
        }

        /// <summary>
        /// Creates attributes from a standard dictionary of keys and values.
        /// </summary>
        /// <param name="dictionary">Key-value dictionary.</param>
        /// <returns name="attributes">The NtsAttributes record.</returns>
        public static NtsAttributes FromDictionary(IDictionary<string, object> dictionary)
        {
            if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));

            var table = new AttributesTable();
            var fields = new List<NtsField>();

            foreach (var kvp in dictionary)
            {
                var fieldType = InferType(kvp.Value);
                var field = NtsField.ByNameAndType(kvp.Key, fieldType);

                table.Add(field.Name, CoerceValue(kvp.Value, field));
                fields.Add(field);
            }

            var schema = NtsSchema.ByFields(fields);
            return new NtsAttributes(table, schema);
        }

        /// <summary>
        /// Creates an empty attribute record.
        /// </summary>
        public static NtsAttributes Empty()
        {
            return new NtsAttributes(new AttributesTable(), null);
        }

        #endregion

        #region Action / Query Nodes

        /// <summary>
        /// Gets an attribute value by field name.
        /// </summary>
        /// <param name="fieldName">Field name.</param>
        /// <returns name="value">The attribute value, or null if not found.</returns>
        public object GetValue(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return null;
            return InternalTable.Exists(fieldName) ? InternalTable[fieldName] : null;
        }

        /// <summary>
        /// Sets or updates an attribute value.
        /// </summary>
        /// <param name="fieldName">Field name.</param>
        /// <param name="value">Value to set.</param>
        /// <returns name="attributes">The updated NtsAttributes instance.</returns>
        public NtsAttributes SetValue(string fieldName, object value)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) throw new ArgumentException("Field name cannot be null or empty.");
            InternalTable[fieldName] = value;
            return this;
        }

        /// <summary>
        /// Converts the attribute record into a native Dynamo Dictionary.
        /// </summary>
        /// <returns name="dictionary">Dynamo-compatible dictionary.</returns>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>();
            foreach (var name in InternalTable.GetNames())
            {
                dict[name] = InternalTable[name];
            }
            return dict;
        }

        #endregion

        #region Helpers

        private static object CoerceValue(object rawValue, NtsField field)
        {
            if (rawValue == null) return DBNull.Value;

            switch (field.Type)
            {
                case NtsFieldType.String:
                    string s = rawValue.ToString();
                    return s.Length > field.Length ? s.Substring(0, field.Length) : s;

                case NtsFieldType.Integer:
                    return Convert.ToInt32(rawValue, CultureInfo.InvariantCulture);

                case NtsFieldType.Double:
                    return Convert.ToDouble(rawValue, CultureInfo.InvariantCulture);

                case NtsFieldType.Boolean:
                    return Convert.ToBoolean(rawValue);

                case NtsFieldType.Date:
                    return Convert.ToDateTime(rawValue, CultureInfo.InvariantCulture);

                default:
                    return rawValue;
            }
        }

        private static NtsFieldType InferType(object value)
        {
            if (value is int || value is short || value is long) return NtsFieldType.Integer;
            if (value is double || value is float || value is decimal) return NtsFieldType.Double;
            if (value is bool) return NtsFieldType.Boolean;
            if (value is DateTime) return NtsFieldType.Date;
            return NtsFieldType.String;
        }

        #endregion
    }
}