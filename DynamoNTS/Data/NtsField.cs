using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Infers the definition of an NtsField by analyzing all values in a column.
        /// Promotes Integer + Double to Double, promotes out-of-range 64-bit integers to Double,
        /// calculates the maximum actual length for strings, and assigns String by default if all values are null.
        /// </summary>
        /// <param name="name">Field Name.</param>
        /// <param name="values">Field Values.</param>
        /// <returns name="field">NtsField instance.</returns>
        public static NtsField InferField(string name, IEnumerable<object?> values)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(name));

            string trimmedName = name.Trim();
            if (values == null)
            {
                return ByNameAndType(trimmedName, NtsFieldType.String, 50);
            }

            bool hasInt = false;
            bool hasDouble = false;
            bool hasBool = false;
            bool hasDate = false;
            bool hasString = false;
            bool hasOther = false;
            int maxLen = 0;

            foreach (var val in values)
            {
                if (val == null || val is DBNull) continue;

                string s = val.ToString() ?? string.Empty;
                if (s.Length > maxLen)
                {
                    maxLen = s.Length;
                }

                if (val is int || val is short || val is byte || val is sbyte || val is ushort)
                {
                    hasInt = true;
                }
                else if (val is long l)
                {
                    if (l >= int.MinValue && l <= int.MaxValue)
                        hasInt = true;
                    else
                        hasDouble = true;
                }
                else if (val is uint ui)
                {
                    if (ui <= int.MaxValue)
                        hasInt = true;
                    else
                        hasDouble = true;
                }
                else if (val is ulong ul)
                {
                    if (ul <= (ulong)int.MaxValue)
                        hasInt = true;
                    else
                        hasDouble = true;
                }
                else if (val is double || val is float || val is decimal)
                {
                    hasDouble = true;
                }
                else if (val is bool)
                {
                    hasBool = true;
                }
                else if (val is DateTime)
                {
                    hasDate = true;
                }
                else if (val is string)
                {
                    hasString = true;
                }
                else
                {
                    hasOther = true;
                }
            }

            NtsFieldType resolvedType;
            if (!hasInt && !hasDouble && !hasBool && !hasDate && !hasString && !hasOther)
            {
                resolvedType = NtsFieldType.String;
            }
            else if (hasString || hasOther)
            {
                resolvedType = NtsFieldType.String;
            }
            else if (hasBool)
            {
                resolvedType = (!hasInt && !hasDouble && !hasDate) ? NtsFieldType.Boolean : NtsFieldType.String;
            }
            else if (hasDate)
            {
                resolvedType = (!hasInt && !hasDouble && !hasBool) ? NtsFieldType.Date : NtsFieldType.String;
            }
            else if (hasDouble || (hasInt && hasDouble))
            {
                resolvedType = NtsFieldType.Double;
            }
            else if (hasInt)
            {
                resolvedType = NtsFieldType.Integer;
            }
            else
            {
                resolvedType = NtsFieldType.String;
            }

            int length = maxLen > 0 ? Math.Max(1, Math.Min(254, maxLen)) : 50;
            return ByNameAndType(trimmedName, resolvedType, length);
        }

        #endregion

        public override string ToString()
        {
            return $"NtsField({Name}, {Type}, Len={Length})";
        }
    }
}