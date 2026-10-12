using Autodesk.DesignScript.Runtime;
using DynamoNTS.Geometry;
using DynamoNTS.Layers;
using NetTopologySuite.Features;
using System;
using System.Collections.Generic;

namespace DynamoNTS.Features
{
    /// <summary>
    /// Represents a complete GIS feature combining topological geometry and tabular attributes.
    /// </summary>
    public class NtsFeature
    {
        /// <summary>
        /// Internal NetTopologySuite Feature instance.
        /// Hidden from the Dynamo user interface.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal Feature InternalFeature { get; }

        /// <summary>
        /// Gets the topological geometry associated with this feature.
        /// </summary>
        public NtsGeometry Geometry { get; }

        /// <summary>
        /// Gets the attribute record associated with this feature.
        /// </summary>
        public NtsAttributes Attributes { get; }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsFeature(Feature feature, NtsGeometry geometry, NtsAttributes attributes)
        {
            InternalFeature = feature ?? throw new ArgumentNullException(nameof(feature));
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            Attributes = attributes ?? NtsAttributes.Empty();
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsFeature combining an NtsGeometry and an NtsAttributes record.
        /// </summary>
        /// <param name="geometry">The topological geometry.</param>
        /// <param name="attributes">The attribute record.</param>
        /// <returns name="feature">The resulting NtsFeature.</returns>
        public static NtsFeature Create(NtsGeometry geometry, NtsAttributes attributes)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var attrs = attributes ?? NtsAttributes.Empty();
            var ntsFeature = new Feature(geometry.InternalGeometry, attrs.InternalTable);

            return new NtsFeature(ntsFeature, geometry, attrs);
        }

        /// <summary>
        /// Creates an NtsFeature from an NtsGeometry and a dictionary of properties.
        /// </summary>
        /// <param name="geometry">The topological geometry.</param>
        /// <param name="properties">Dictionary containing attribute keys and values.</param>
        /// <returns name="feature">The resulting NtsFeature.</returns>
        public static NtsFeature ByGeometryAndDictionary(NtsGeometry geometry, IDictionary<string, object> properties)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var attrs = properties != null ? NtsAttributes.FromDictionary(properties) : NtsAttributes.Empty();
            return Create(geometry, attrs);
        }

        /// <summary>
        /// Creates an NtsFeature with a default integer FID attribute.
        /// </summary>
        /// <param name="geometry">The topological geometry.</param>
        /// <param name="fid">Feature ID number.</param>
        /// <returns name="feature">The resulting NtsFeature.</returns>
        public static NtsFeature ByGeometryAndId(NtsGeometry geometry, int fid = 1)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var field = NtsField.ByNameAndType("FID", NtsFieldType.Integer);
            var schema = NtsSchema.ByFields(new[] { field });
            var attrs = NtsAttributes.BySchemaAndValues(schema, new object[] { fid });

            return Create(geometry, attrs);
        }

        #endregion

        #region Action / Query Nodes

        /// <summary>
        /// Gets an attribute value from the feature by column name.
        /// </summary>
        /// <param name="fieldName">Field name.</param>
        /// <returns name="value">The attribute value.</returns>
        public object GetValue(string fieldName)
        {
            return Attributes?.GetValue(fieldName);
        }

        /// <summary>
        /// Sets or updates an attribute value on the feature.
        /// </summary>
        /// <param name="fieldName">Field name.</param>
        /// <param name="value">Value to set.</param>
        /// <returns name="feature">The updated NtsFeature.</returns>
        public NtsFeature SetValue(string fieldName, object value)
        {
            Attributes?.SetValue(fieldName, value);
            return this;
        }

        #endregion

        #region Internal Factory

        /// <summary>
        /// Converts a raw NetTopologySuite Feature into an NtsFeature.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal static NtsFeature FromNtsFeature(Feature ntsFeature, NtsSchema schema = null)
        {
            if (ntsFeature == null) return null;

            var geom = NtsGeometry.WrapGeometry(ntsFeature.Geometry);
            if (geom == null) return null;

            var table = ntsFeature.Attributes is AttributesTable at
                ? at
                : new AttributesTable();

            if (table.Count == 0 && ntsFeature.Attributes != null)
            {
                foreach (var name in ntsFeature.Attributes.GetNames())
                {
                    table.Add(name, ntsFeature.Attributes[name]);
                }
            }

            var attributes = new NtsAttributes(table, schema);
            return new NtsFeature(ntsFeature, geom, attributes);
        }

        #endregion
    }
}
