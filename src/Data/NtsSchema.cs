using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamoNTS.Data
{
    /// <summary>
    /// Represents the formal tabular attribute schema (ordered collection of NtsField definitions) for a GIS layer.
    /// </summary>
    public class NtsSchema
    {
        [IsVisibleInDynamoLibrary(false)]
        private readonly List<NtsField> _fields;

        [IsVisibleInDynamoLibrary(false)]
        internal IReadOnlyList<NtsField> InternalFields => _fields;

        /// <summary>
        /// Gets the list of column names defined in this schema.
        /// </summary>
        public List<string> FieldNames => _fields.Select(f => f.Name).ToList();

        /// <summary>
        /// Gets the total number of fields registered in this schema.
        /// </summary>
        public int FieldCount => _fields.Count;

        /// <summary>
        /// Gets a defensive copy of all NtsField definitions.
        /// </summary>
        public List<NtsField> Fields => new List<NtsField>(_fields);

        [IsVisibleInDynamoLibrary(false)]
        internal NtsSchema(IEnumerable<NtsField> fields)
        {
            _fields = fields?.ToList() ?? new List<NtsField>();
        }

        #region Create Nodes

        public static NtsSchema ByFields(IList<NtsField> fields)
        {
            if (fields == null || fields.Count == 0)
                return NtsSchema.Empty();

            var distinctFields = new List<NtsField>();
            var registeredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in fields)
            {
                if (field == null) continue;

                if (!registeredNames.Add(field.Name))
                    throw new ArgumentException($"Duplicate field name detected: '{field.Name}'.");

                distinctFields.Add(field);
            }

            return new NtsSchema(distinctFields);
        }

        public static NtsSchema Empty()
        {
            return new NtsSchema(Enumerable.Empty<NtsField>());
        }

        #endregion

        #region Action Nodes

        public NtsSchema AddField(NtsField field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            if (_fields.Any(f => f.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Field '{field.Name}' already exists in the schema.");

            var updatedList = new List<NtsField>(_fields) { field };
            return new NtsSchema(updatedList);
        }

        public NtsSchema RemoveField(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));

            var updatedList = _fields
                .Where(f => !f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new NtsSchema(updatedList);
        }

        #endregion

        #region Query Nodes

        public NtsField? FieldByName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return null;
            return _fields.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsField? GetField(string fieldName) => FieldByName(fieldName);

        #endregion

        /// <inheritdoc />
        public override string ToString()
        {
            return $"NtsSchema({FieldCount} Fields: {string.Join(", ", FieldNames)})";
        }
    }
}