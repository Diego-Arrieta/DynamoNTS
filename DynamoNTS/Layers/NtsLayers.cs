using Autodesk.DesignScript.Runtime;
using DynamoNTS.Features;
using DynamoNTS.Geometry;
using DynamoNTS.Projections;
using NetTopologySuite.Features;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamoNTS.Layers
{
    /// <summary>
    /// Represents a complete GIS vector layer containing a unified schema, a coordinate system, and spatial features.
    /// </summary>
    public class NtsLayer
    {
        [IsVisibleInDynamoLibrary(false)]
        internal FeatureCollection InternalCollection { get; }

        /// <summary>
        /// Gets the layer name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the attribute schema shared across all features in this layer.
        /// </summary>
        public NtsSchema Schema { get; }

        /// <summary>
        /// Gets the Coordinate Reference System (CRS) of the layer, if defined.
        /// </summary>
        public CoordinateSystem CoordinateSystem { get; }

        /// <summary>
        /// Gets all features contained in this layer.
        /// </summary>
        public List<NtsFeature> Features { get; }

        /// <summary>
        /// Gets the total number of features in the layer.
        /// </summary>
        public int Count => Features.Count;

        [IsVisibleInDynamoLibrary(false)]
        internal NtsLayer(
            string name,
            NtsSchema schema,
            IEnumerable<NtsFeature> features,
            CoordinateSystem crs = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Layer" : name;
            Schema = schema;
            CoordinateSystem = crs;
            Features = features?.Where(f => f != null).ToList() ?? new List<NtsFeature>();

            InternalCollection = new FeatureCollection();
            foreach (var feature in Features)
            {
                InternalCollection.Add(feature.InternalFeature);
            }
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsLayer from a collection of features and an optional Coordinate Reference System.
        /// Infers the schema automatically from the features.
        /// </summary>
        /// <param name="features">Collection of NtsFeature entities.</param>
        /// <param name="name">Layer identifier name.</param>
        /// <param name="coordinateSystem">Coordinate Reference System (CRS) of the layer.</param>
        /// <returns name="layer">The constructed NtsLayer.</returns>
        public static NtsLayer ByFeatures(
            IList<NtsFeature> features,
            string name = "Layer",
            CoordinateSystem coordinateSystem = null)
        {
            if (features == null || features.Count == 0)
                throw new ArgumentException("The feature collection cannot be null or empty.", nameof(features));

            var firstWithSchema = features.FirstOrDefault(f => f.Attributes?.Schema != null);
            NtsSchema schema = firstWithSchema?.Attributes.Schema;

            if (schema == null && features.Count > 0 && features[0].Attributes != null)
            {
                var dict = features[0].Attributes.ToDictionary();
                schema = NtsAttributes.FromDictionary(dict).Schema;
            }

            return new NtsLayer(name, schema, features, coordinateSystem);
        }

        /// <summary>
        /// Creates an NtsLayer enforcing a strict NtsSchema across all features.
        /// </summary>
        /// <param name="schema">The formal layer schema.</param>
        /// <param name="features">Collection of NtsFeature entities.</param>
        /// <param name="name">Layer identifier name.</param>
        /// <param name="coordinateSystem">Coordinate Reference System (CRS) of the layer.</param>
        /// <returns name="layer">The constructed NtsLayer.</returns>
        public static NtsLayer BySchemaAndFeatures(
            NtsSchema schema,
            IList<NtsFeature> features,
            string name = "Layer",
            CoordinateSystem coordinateSystem = null)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (features == null) throw new ArgumentNullException(nameof(features));

            return new NtsLayer(name, schema, features, coordinateSystem);
        }

        /// <summary>
        /// Creates an NtsLayer directly from pure geometries with a default incremental FID column.
        /// </summary>
        /// <param name="geometries">Collection of topological geometries.</param>
        /// <param name="name">Layer identifier name.</param>
        /// <param name="coordinateSystem">Coordinate Reference System (CRS) of the layer.</param>
        /// <returns name="layer">The constructed NtsLayer.</returns>
        public static NtsLayer ByGeometries(
            IList<NtsGeometry> geometries,
            string name = "Layer",
            CoordinateSystem coordinateSystem = null)
        {
            if (geometries == null || geometries.Count == 0)
                throw new ArgumentException("The geometry collection cannot be null or empty.", nameof(geometries));

            var field = NtsField.ByNameAndType("FID", NtsFieldType.Integer);
            var schema = NtsSchema.ByFields(new[] { field });
            var featureList = new List<NtsFeature>();

            for (int i = 0; i < geometries.Count; i++)
            {
                var geom = geometries[i];
                if (geom == null) continue;

                var attrs = NtsAttributes.BySchemaAndValues(schema, new object[] { i + 1 });
                featureList.Add(NtsFeature.Create(geom, attrs));
            }

            return new NtsLayer(name, schema, featureList, coordinateSystem);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Extracts all topological geometries from the layer.
        /// </summary>
        /// <returns name="geometries">List of NtsGeometry instances.</returns>
        public List<NtsGeometry> GetGeometries()
        {
            return Features
                .Where(f => f.Geometry != null)
                .Select(f => f.Geometry)
                .ToList();
        }

        /// <summary>
        /// Filters layer features by checking equality against an attribute field value.
        /// </summary>
        /// <param name="fieldName">Field column name.</param>
        /// <param name="value">Target value to match.</param>
        /// <returns name="filteredLayer">A new NtsLayer containing only matching features.</returns>
        public NtsLayer FilterByAttribute(string fieldName, object value)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));

            var matched = Features.Where(f =>
            {
                var val = f.GetValue(fieldName);
                if (val == null && value == null) return true;
                if (val == null || value == null) return false;
                return val.ToString().Equals(value.ToString(), StringComparison.OrdinalIgnoreCase);
            }).ToList();

            return new NtsLayer($"{Name}_Filtered", Schema, matched, CoordinateSystem);
        }

        /// <summary>
        /// Reprojects the entire layer into a target Coordinate Reference System.
        /// </summary>
        /// <param name="targetCRS">The target Coordinate Reference System.</param>
        /// <returns name="reprojectedLayer">A new NtsLayer with reprojected geometries.</returns>
        public NtsLayer Reproject(CoordinateSystem targetCRS)
        {
            if (CoordinateSystem == null)
                throw new InvalidOperationException("Source CoordinateSystem is not assigned to the layer.");

            if (targetCRS == null)
                throw new ArgumentNullException(nameof(targetCRS));

            var reprojectedFeatures = new List<NtsFeature>();

            foreach (var feature in Features)
            {
                reprojectedFeatures.Add(Reprojector.TransformFeature(feature, CoordinateSystem, targetCRS));
            }

            return new NtsLayer(Name, Schema, reprojectedFeatures, targetCRS);
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Computes the bounding envelope polygon covering all geometries in the layer.
        /// </summary>
        /// <returns name="envelope">An NtsGeometry representing the layer boundary extents.</returns>
        public NtsGeometry Extents()
        {
            var validGeoms = Features
                .Where(f => f.Geometry?.InternalGeometry != null && !f.Geometry.InternalGeometry.IsEmpty)
                .Select(f => f.Geometry.InternalGeometry)
                .ToList();

            if (validGeoms.Count == 0) return null;

            var env = new NetTopologySuite.Geometries.Envelope();
            foreach (var geom in validGeoms)
            {
                env.ExpandToInclude(geom.EnvelopeInternal);
            }

            var factory = NetTopologySuite.Geometries.GeometryFactory.Default;
            var poly = factory.ToGeometry(env);

            return NtsGeometry.WrapGeometry(poly);
        }

        #endregion
    }
}