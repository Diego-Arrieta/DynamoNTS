using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
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
        internal List<NtsField> InternalFields { get; }

        /// <summary>
        /// Gets the list of column names defined in this schema.
        /// </summary>
        public List<string> FieldNames => InternalFields.Select(f => f.Name).ToList();

        /// <summary>
        /// Gets the total number of fields registered in this schema.
        /// </summary>
        public int FieldCount => InternalFields.Count;

        /// <summary>
        /// Gets a copy of all NtsField definitions.
        /// </summary>
        public List<NtsField> Fields => new List<NtsField>(InternalFields);

        [IsVisibleInDynamoLibrary(false)]
        internal NtsSchema(IEnumerable<NtsField> fields)
        {
            InternalFields = fields?.ToList() ?? new List<NtsField>();
        }

        #region Create Nodes

        /// <summary>
        /// Creates a formal attribute schema from a collection of NtsField definitions, ensuring unique column names.
        /// </summary>
        /// <param name="fields">Collection of NtsField instances.</param>
        /// <returns name="schema">The constructed immutable NtsSchema.</returns>
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

        /// <summary>
        /// Creates an empty schema without fields.
        /// </summary>
        /// <returns name="schema">An empty NtsSchema.</returns>
        public static NtsSchema Empty()
        {
            return new NtsSchema(Enumerable.Empty<NtsField>());
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Produces a new schema by appending an additional NtsField column.
        /// </summary>
        /// <param name="field">The NtsField definition to add.</param>
        /// <returns name="schema">A new NtsSchema containing the added field.</returns>
        public NtsSchema AddField(NtsField field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            if (InternalFields.Any(f => f.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Field '{field.Name}' already exists in the schema.");

            var updatedList = new List<NtsField>(InternalFields) { field };
            return new NtsSchema(updatedList);
        }

        /// <summary>
        /// Produces a new schema by removing an existing field by column name.
        /// </summary>
        /// <param name="fieldName">The name of the column to remove.</param>
        /// <returns name="schema">A new NtsSchema without the specified field.</returns>
        public NtsSchema RemoveField(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));

            var updatedList = InternalFields
                .Where(f => !f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new NtsSchema(updatedList);
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Retrieves an NtsField specification by column name (case-insensitive).
        /// </summary>
        /// <param name="fieldName">Column name to search.</param>
        /// <returns name="field">The matching NtsField or null if not found.</returns>
        public NtsField? FieldByName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return null;
            return InternalFields.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsField? GetField(string fieldName) => FieldByName(fieldName);

        #endregion

        public override string ToString()
        {
            return $"NtsSchema({FieldCount} Fields: {string.Join(", ", FieldNames)})";
        }
    }
}