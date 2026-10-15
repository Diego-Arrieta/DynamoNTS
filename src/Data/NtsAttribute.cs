using Autodesk.DesignScript.Runtime;
using System;
using System.Globalization;

namespace DynamoNTS.Data
{
    /// <summary>
    /// Represents an atomic attribute cell consisting of a field specification and its typed value.
    /// </summary>
    public class NtsAttribute
    {
        /// <summary>
        /// The column definition and metadata associated with this attribute.
        /// </summary>
        public NtsField Field { get; }

        /// <summary>
        /// The field column name.
        /// </summary>
        public string Name => Field.Name;

        /// <summary>
        /// The data type of the attribute.
        /// </summary>
        public NtsFieldType Type => Field.Type;

        /// <summary>
        /// The typed value stored in this cell.
        /// </summary>
        public object? Value { get; }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsAttribute(NtsField field, object? value)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Value = CoerceValue(value, field);
        }

        #region Create Nodes

        /// <summary>
        /// Creates an atomic attribute cell validating the value against an explicit NtsField definition.
        /// </summary>
        /// <param name="field">The target field definition.</param>
        /// <param name="value">The raw value to validate and store.</param>
        /// <returns name="attribute">The validated NtsAttribute instance.</returns>
        public static NtsAttribute ByField(NtsField field, object? value)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            return new NtsAttribute(field, value);
        }

        /// <summary>
        /// Creates an atomic attribute cell inferring the data type from the provided value.
        /// </summary>
        /// <param name="fieldName">The name of the attribute column.</param>
        /// <param name="value">The value to store.</param>
        /// <returns name="attribute">The constructed NtsAttribute instance.</returns>
        public static NtsAttribute ByNameAndValue(string fieldName, object? value)
        {
            var fieldType = InferType(value);
            var field = NtsField.ByNameAndType(fieldName, fieldType);
            return new NtsAttribute(field, value);
        }

        #endregion

        #region Helpers

        [IsVisibleInDynamoLibrary(false)]
        internal static object? CoerceValue(object? rawValue, NtsField field)
        {
            if (rawValue == null || rawValue is DBNull) return null;

            try
            {
                switch (field.Type)
                {
                    case NtsFieldType.String:
                        return rawValue.ToString() ?? string.Empty;

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
            catch (Exception ex)
            {
                throw new InvalidCastException($"Unable to convert value '{rawValue}' to type {field.Type} for field '{field.Name}'.", ex);
            }
        }

        [IsVisibleInDynamoLibrary(false)]
        internal static NtsFieldType InferType(object? value)
        {
            if (value is int || value is short || value is byte || value is sbyte || value is ushort)
                return NtsFieldType.Integer;

            if (value is long l)
                return (l >= int.MinValue && l <= int.MaxValue) ? NtsFieldType.Integer : NtsFieldType.Double;

            if (value is uint ui)
                return (ui <= int.MaxValue) ? NtsFieldType.Integer : NtsFieldType.Double;

            if (value is ulong ul)
                return (ul <= (ulong)int.MaxValue) ? NtsFieldType.Integer : NtsFieldType.Double;

            if (value is double || value is float || value is decimal)
                return NtsFieldType.Double;

            if (value is bool)
                return NtsFieldType.Boolean;

            if (value is DateTime)
                return NtsFieldType.Date;

            return NtsFieldType.String;
        }

        #endregion

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Name}: {Value ?? "null"} ({Type})";
        }
    }
}