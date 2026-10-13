using Autodesk.DesignScript.Runtime;
using System;

namespace DynamoNTS.Data
{
    /// <summary>
    /// Defines column schema metadata and constraints for a GIS attribute field.
    /// </summary>
    public class NtsField
    {
        /// <summary>
        /// Field column name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Data type of the attribute column.
        /// </summary>
        public NtsFieldType Type { get; }

        /// <summary>
        /// Maximum character length for string fields (default: 50, max: 254).
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
        /// <param name="name">Column name (supports full-length descriptive names).</param>
        /// <param name="type">Attribute column data type.</param>
        /// <param name="length">Maximum string length (default: 50, max: 254).</param>
        /// <returns name="field">The configured NtsField instance.</returns>
        public static NtsField ByNameAndType(string name, NtsFieldType type, int length = 50)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(name));

            string trimmedName = name.Trim();

            if (length < 1) length = 1;
            if (length > 254) length = 254;

            return new NtsField(trimmedName, type, length);
        }

        #endregion

        public override string ToString()
        {
            return $"NtsField({Name}, {Type}, Len={Length})";
        }
    }
}