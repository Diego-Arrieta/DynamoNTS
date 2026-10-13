using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
using DynamoNTS.Geometry;
using DynamoNTS.Data;
using DynamoNTS.Projections;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NtsGeom = NetTopologySuite.Geometries.Geometry;

namespace DynamoNTS.IO
{
    /// <summary>
    /// Provides methods to import and export GeoJSON data for layers and isolated geometries.
    /// </summary>
    public static class GeoJSON
    {
        #region Layer Level (FeatureCollection)

        /// <summary>
        /// Reads a GeoJSON file or JSON string containing a FeatureCollection and builds an NtsLayer.
        /// </summary>
        /// <param name="filePathOrJson">Path to the .geojson file or direct JSON string.</param>
        /// <param name="crsOverride">Optional Coordinate Reference System (defaults to WGS84 EPSG:4326).</param>
        /// <returns name="layer">The reconstructed NtsLayer.</returns>
        public static NtsLayer ReadLayer(
            string filePathOrJson,
            [DefaultArgument("null")] NtsCoordinateSystem? crsOverride = null)
        {
            if (string.IsNullOrWhiteSpace(filePathOrJson))
                throw new ArgumentException("Input path or GeoJSON text cannot be null or empty.", nameof(filePathOrJson));

            string jsonContent = File.Exists(filePathOrJson)
                ? File.ReadAllText(filePathOrJson)
                : filePathOrJson;

            var reader = new GeoJsonReader();
            var featureCollection = reader.Read<FeatureCollection>(jsonContent);

            if (featureCollection == null || featureCollection.Count == 0)
                throw new InvalidOperationException("No features could be parsed from the provided GeoJSON.");

            var features = new List<NtsFeature>();
            var fieldsMap = new Dictionary<string, NtsField>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in featureCollection)
            {
                if (f.Geometry == null || f.Geometry.IsEmpty) continue;

                var wrappedGeom = NtsGeometry.WrapGeometry(f.Geometry);
                if (wrappedGeom == null) continue;

                var table = new AttributesTable();
                if (f.Attributes != null)
                {
                    foreach (var name in f.Attributes.GetNames())
                    {
                        var val = f.Attributes[name];
                        table.Add(name, val);

                        if (!fieldsMap.ContainsKey(name))
                        {
                            fieldsMap[name] = NtsField.ByNameAndType(name, NtsAttribute.InferType(val));
                        }
                    }
                }

                var concreteFeature = new Feature(f.Geometry, table);
                var attrs = new NtsRecord(table, NtsSchema.Empty());
                features.Add(new NtsFeature(concreteFeature, wrappedGeom, attrs));
            }

            var schema = NtsSchema.ByFields(fieldsMap.Values.ToList());
            var crs = crsOverride ?? NtsCoordinateSystem.WGS84;
            string layerName = File.Exists(filePathOrJson) ? Path.GetFileNameWithoutExtension(filePathOrJson) : "GeoJsonLayer";

            return NtsLayer.ByFeatures(features, layerName, crs, schema);
        }

        /// <summary>
        /// Writes an NtsLayer to disk in GeoJSON FeatureCollection format.
        /// </summary>
        /// <param name="layer">The NtsLayer to export.</param>
        /// <param name="filePath">Target destination path (.geojson).</param>
        /// <returns name="success">True if export succeeded.</returns>
        public static bool WriteLayer(NtsLayer layer, string filePath)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var writer = new GeoJsonWriter();
            string json = writer.Write(layer.InternalCollection);

            File.WriteAllText(filePath, json);
            return true;
        }

        #endregion

        #region Geometry Level (Isolated Geometry Text)

        /// <summary>
        /// Creates an NtsGeometry directly from an isolated GeoJSON geometry text fragment.
        /// </summary>
        /// <param name="jsonText">GeoJSON geometry text (e.g., {"type":"Point","coordinates":[0,0]}).</param>
        /// <returns name="geometry">The imported NtsGeometry.</returns>
        public static NtsGeometry? FromGeometry(string jsonText)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
                throw new ArgumentException("GeoJSON string cannot be null or empty.", nameof(jsonText));

            var reader = new GeoJsonReader();
            var ntsGeom = reader.Read<NtsGeom>(jsonText);

            return NtsGeometry.WrapGeometry(ntsGeom);
        }

        /// <summary>
        /// Converts an NtsGeometry into its isolated GeoJSON string representation.
        /// </summary>
        /// <param name="geometry">The topological geometry to serialize.</param>
        /// <returns name="jsonText">The GeoJSON text representation.</returns>
        public static string ToGeometry(NtsGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var writer = new GeoJsonWriter();
            return writer.Write(geometry.InternalGeometry);
        }

        #endregion
    }
}