using Autodesk.DesignScript.Runtime;
using System;
using System.Text.RegularExpressions;

namespace DynamoNTS.Fields
{
    /// <summary>
    /// Defines column schema metadata and constraints for a GIS attribute field.
    /// </summary>
    public class NtsField
    {
        /// <summary>
        /// Field column name (sanitized and trimmed for DBF/GIS compatibility).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Data type of the attribute column.
        /// </summary>
        public NtsFieldType Type { get; }

        /// <summary>
        /// Maximum character length for string fields (max 254 for DBF specifications).
        /// </summary>
        public int Length { get; }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsField(string name, NtsFieldType type, int length = 50)
        {
            Name = name;
            Type = type;
            Length = length;
        }

        #region Create Nodes

        /// <summary>
        /// Creates a GIS attribute field definition.
        /// </summary>
        /// <param name="name">Column name (alphanumeric and underscores; sanitized and trimmed to 10 characters for DBF compatibility).</param>
        /// <param name="type">Attribute column data type.</param>
        /// <param name="length">Maximum string length (default: 50, max: 254).</param>
        /// <returns name="field">The configured NtsField instance.</returns>
        public static NtsField ByNameAndType(string name, NtsFieldType type, int length = 50)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(name));

            // Sanitize column name for DBF / Shapefile compatibility
            string sanitized = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_]", "_");

            // DBF 10-character limit enforcement
            if (sanitized.Length > 10)
            {
                sanitized = sanitized.Substring(0, 10);
            }

            if (length < 1) length = 1;
            if (length > 254) length = 254;

            return new NtsField(sanitized, type, length);
        }

        #endregion

        public override string ToString()
        {
            return $"NtsField({Name}, {Type}, Len={Length})";
        }
    }
}