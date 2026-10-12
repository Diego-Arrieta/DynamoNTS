using Autodesk.DesignScript.Runtime;
using DynamoNTS.Features;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamoNTS.Layers
{
    /// <summary>
    /// Defines an attribute table schema (collection of fields) shared across spatial features in a layer.
    /// </summary>
    public class NtsSchema
    {
        [IsVisibleInDynamoLibrary(false)]
        internal List<NtsField> InternalFields { get; }

        /// <summary>
        /// Gets the list of field names defined in this schema.
        /// </summary>
        public List<string> FieldNames => InternalFields.Select(f => f.Name).ToList();

        /// <summary>
        /// Gets the total number of fields in the schema.
        /// </summary>
        public int FieldCount => InternalFields.Count;

        /// <summary>
        /// Gets the list of NtsField definitions.
        /// </summary>
        public List<NtsField> Fields => new List<NtsField>(InternalFields);

        [IsVisibleInDynamoLibrary(false)]
        internal NtsSchema(IEnumerable<NtsField> fields)
        {
            InternalFields = fields?.ToList() ?? new List<NtsField>();
        }

        #region Create Nodes

        /// <summary>
        /// Creates an attribute schema from a collection of NtsField definitions.
        /// </summary>
        /// <param name="fields">List of NtsField instances.</param>
        /// <returns name="schema">The created NtsSchema.</returns>
        public static NtsSchema ByFields(IList<NtsField> fields)
        {
            if (fields == null || fields.Count == 0)
                throw new ArgumentException("Schema must contain at least one field.", nameof(fields));

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

        #endregion

        #region Action Nodes

        /// <summary>
        /// Gets a field definition by its column name.
        /// </summary>
        /// <param name="fieldName">Column name.</param>
        /// <returns name="field">The matching NtsField or null.</returns>
        public NtsField GetField(string fieldName)
        {
            return InternalFields.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}