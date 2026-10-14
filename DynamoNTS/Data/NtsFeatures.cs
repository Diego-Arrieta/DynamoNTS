using Autodesk.DesignScript.Runtime;
using DynamoNTS.Geometry;
using DynamoNTS.Data;
using NetTopologySuite.Features;
using System;
using System.Collections.Generic;

namespace DynamoNTS.Data
{
    /// <summary>
    /// Represents a complete GIS feature combining topological geometry with a structured alphanumeric record.
    /// </summary>
    public class NtsFeature
    {
        [IsVisibleInDynamoLibrary(false)]
        internal IFeature InternalFeature { get; }

        /// <summary>
        /// Topological geometry associated with this feature.
        /// </summary>
        public NtsGeometry Geometry { get; }

        /// <summary>
        /// Tabular alphanumeric record of attributes for this feature.
        /// </summary>
        public NtsRecord Record { get; }

        /// <summary>
        /// The formal attribute schema governing this feature.
        /// </summary>
        public NtsSchema Schema => Record.Schema;

        [IsVisibleInDynamoLibrary(false)]
        internal NtsFeature(IFeature feature, NtsGeometry geometry, NtsRecord record)
        {
            InternalFeature = feature ?? throw new ArgumentNullException(nameof(feature));
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            Record = record ?? NtsRecord.Empty();
        }

        #region Create Nodes

        /// <summary>
        /// Creates a template feature using a geometry and a formal schema (fields initialized to null).
        /// </summary>
        /// <param name="geometry">The topological geometry.</param>
        /// <param name="schema">The formal schema template.</param>
        /// <returns name="feature">The newly initialized feature instance.</returns>
        public static NtsFeature Create(NtsGeometry geometry, NtsSchema schema)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            var record = NtsRecord.FromSchema(schema);
            var ntsFeat = new Feature(geometry.InternalGeometry, record.InternalTable);

            return new NtsFeature(ntsFeat, geometry, record);
        }

        /// <summary>
        /// Creates an NtsFeature combining a topological geometry with an existing NtsRecord.
        /// </summary>
        /// <param name="geometry">The topological geometry.</param>
        /// <param name="record">The attribute record.</param>
        /// <returns name="feature">The resulting NtsFeature instance.</returns>
        public static NtsFeature ByRecord(NtsGeometry geometry, NtsRecord record)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var rec = record ?? NtsRecord.Empty();
            var ntsFeat = new Feature(geometry.InternalGeometry, rec.InternalTable);

            return new NtsFeature(ntsFeat, geometry, rec);
        }

        /// <summary>
        /// Creates a feature from a geometry and an explicit list of atomic NtsAttribute cells.
        /// </summary>
        /// <param name="geometry">The topological geometry.</param>
        /// <param name="attributes">Collection of NtsAttribute instances.</param>
        /// <returns name="feature">The resulting NtsFeature instance.</returns>
        public static NtsFeature ByAttributes(NtsGeometry geometry, IList<NtsAttribute> attributes)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var record = NtsRecord.ByAttributes(attributes ?? new List<NtsAttribute>());
            var ntsFeat = new Feature(geometry.InternalGeometry, record.InternalTable);

            return new NtsFeature(ntsFeat, geometry, record);
        }

        /// <summary>
        /// Creates a feature from a geometry and a native Dynamo dictionary.
        /// </summary>
        /// <param name="geometry">The topological geometry.</param>
        /// <param name="properties">Dictionary containing attribute key-value pairs.</param>
        /// <returns name="feature">The resulting NtsFeature instance.</returns>
        public static NtsFeature ByDictionary(NtsGeometry geometry, IDictionary<string, object?> properties)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var record = properties != null ? NtsRecord.FromDictionary(properties) : NtsRecord.Empty();
            var ntsFeat = new Feature(geometry.InternalGeometry, record.InternalTable);

            return new NtsFeature(ntsFeat, geometry, record);
        }

        #endregion

        #region Key Resolution

        /// <summary>
        /// Resolves an attribute column name to its canonical key in this feature's record.
        /// </summary>
        /// <param name="fieldName">Field name to resolve.</param>
        /// <returns name="canonicalKey">The resolved canonical field name.</returns>
        [IsVisibleInDynamoLibrary(false)]
        public string ResolveKey(string fieldName)
        {
            return Record.ResolveKey(fieldName);
        }

        #endregion

        #region Action / Query Nodes

        /// <summary>
        /// Retrieves all atomic NtsAttribute cells from this feature.
        /// </summary>
        /// <returns name="attributes">List of NtsAttribute cells.</returns>
        public List<NtsAttribute> GetAttributes()
        {
            return Record.GetAttributes();
        }

        /// <summary>
        /// Retrieves an individual atomic NtsAttribute cell by column name or NtsField definition.
        /// </summary>
        /// <param name="field">Column name (string) or NtsField instance.</param>
        /// <returns name="attribute">The queried NtsAttribute cell.</returns>
        public NtsAttribute? GetAttribute(object field)
        {
            return Record.GetAttribute(field);
        }

        /// <summary>
        /// Sets or updates an individual atomic NtsAttribute cell in this feature.
        /// </summary>
        /// <param name="attribute">The NtsAttribute cell to set.</param>
        /// <returns name="feature">The updated NtsFeature instance.</returns>
        public NtsFeature SetAttribute(NtsAttribute attribute)
        {
            Record.SetAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Gets an attribute value by field name or NtsField definition.
        /// </summary>
        /// <param name="field">Column name (string) or NtsField instance.</param>
        /// <returns name="value">The attribute value.</returns>
        public object? GetValue(object field)
        {
            return Record.GetValue(field);
        }

        /// <summary>
        /// Sets or updates an attribute value by field name or NtsField definition.
        /// </summary>
        /// <param name="field">Column name (string) or NtsField instance.</param>
        /// <param name="value">The value to assign.</param>
        /// <returns name="feature">The updated NtsFeature instance.</returns>
        public NtsFeature SetValue(object field, object? value)
        {
            Record.SetValue(field, value);
            return this;
        }

        #endregion

        #region Indexers

        /// <summary>
        /// Gets or sets an attribute value directly on the feature using square-bracket indexing by column name.
        /// Example: feature["COTA_FONDO"] = 24.85;
        /// </summary>
        /// <param name="fieldName">Field column name.</param>
        /// <returns>The attribute value, or null if not found.</returns>
        public object? this[string fieldName]
        {
            get => GetValue(fieldName);
            set => SetValue(fieldName, value);
        }

        /// <summary>
        /// Gets or sets an attribute value directly on the feature using square-bracket indexing by formal NtsField.
        /// </summary>
        /// <param name="field">The NtsField definition.</param>
        /// <returns>The attribute value, or null if not found.</returns>
        public object? this[NtsField field]
        {
            get => GetValue(field);
            set => SetValue(field, value);
        }

        #endregion

        [IsVisibleInDynamoLibrary(false)]
        public override string ToString()
        {
            string geomType = Geometry?.GeometryType.ToString() ?? "None";
            return $"NtsFeature(Geom={geomType}, Attrs={Record?.Count ?? 0})";
        }
    }
}