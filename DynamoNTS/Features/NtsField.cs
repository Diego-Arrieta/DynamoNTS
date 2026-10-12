using Autodesk.DesignScript.Runtime;
using System;
using System.Text.RegularExpressions;

namespace DynamoNTS.Features
{
    /// <summary>
    /// Defines the schema metadata for an attribute field column.
    /// </summary>
    public class NtsField
    {
        /// <summary>
        /// Field column name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Field data type.
        /// </summary>
        public NtsFieldType Type { get; }

        /// <summary>
        /// Maximum length for string characters (max 254 for DBF).
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
        /// <param name="name">Column name (alphanumeric; sanitized and trimmed to 10 characters for DBF compatibility).</param>
        /// <param name="type">Data type of the field.</param>
        /// <param name="length">Maximum string length (default: 50, maximum: 254).</param>
        /// <returns name="field">The configured NtsField.</returns>
        public static NtsField ByNameAndType(string name, NtsFieldType type, int length = 50)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(name));

            // Sanitize: letters, numbers and underscores only
            string sanitized = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_]", "_");

            // DBF 10-character limit
            if (sanitized.Length > 10)
            {
                sanitized = sanitized.Substring(0, 10);
            }

            if (length < 1) length = 1;
            if (length > 254) length = 254;

            return new NtsField(sanitized, type, length);
        }

        #endregion
    }
}