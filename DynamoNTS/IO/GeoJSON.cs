using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
using DynamoNTS.Geometry;
using DynamoNTS.Projections;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NtsGeom = NetTopologySuite.Geometries.Geometry;

namespace DynamoNTS.IO
{
    /// <summary>
    /// Provides methods to import and export GeoJSON data for layers and isolated geometries.
    /// Complies with RFC 7946 and supports legacy embedded CRS definitions.
    /// </summary>
    public static class GeoJSON
    {
        #region Layer Level (FeatureCollection)

        /// <summary>
        /// Reads a GeoJSON file or JSON string containing a FeatureCollection and builds an NtsLayer.
        /// Parses embedded CRS definitions if present, infers schema across all features, and reprojects if crsOverride is provided.
        /// </summary>
        /// <param name="filePathOrJson">Path to the .geojson file or direct JSON string.</param>
        /// <param name="crsOverride">Optional target Coordinate Reference System to reproject the layer into.</param>
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

            var embeddedCrs = ExtractEmbeddedCrs(jsonContent);
            var sourceCrs = embeddedCrs ?? NtsCoordinateSystem.WGS84;
            string crsSource = embeddedCrs != null ? "embedded" : "none";

            var features = new List<NtsFeature>();
            var fieldNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                        if (seenNames.Add(name))
                        {
                            fieldNames.Add(name);
                        }
                    }
                }

                var concreteFeature = new Feature(f.Geometry, table);
                var attrs = new NtsRecord(table, NtsSchema.Empty());
                features.Add(new NtsFeature(concreteFeature, wrappedGeom, attrs));
            }

            if (features.Count == 0)
                throw new InvalidOperationException("No valid geometries could be parsed from the GeoJSON features.");

            var inferredFields = new List<NtsField>();
            foreach (var name in fieldNames)
            {
                var values = features.Select(feat => feat.GetValue(name));
                inferredFields.Add(NtsField.InferField(name, values));
            }

            var schema = inferredFields.Count > 0
                ? NtsSchema.ByFields(inferredFields)
                : NtsSchema.Empty();

            string layerName = File.Exists(filePathOrJson)
                ? Path.GetFileNameWithoutExtension(filePathOrJson)
                : "GeoJsonLayer";

            var nativeLayer = NtsLayer.ByFeatures(features, layerName, sourceCrs, schema);
            nativeLayer.CrsSource = crsSource;

            if (crsOverride != null)
            {
                if (!sourceCrs.IsEquivalentTo(crsOverride))
                {
                    var reprojected = nativeLayer.Reproject(crsOverride);
                    reprojected.CrsSource = "override";
                    return reprojected;
                }

                var overridden = nativeLayer.SetCoordinateSystem(crsOverride);
                overridden.CrsSource = "override";
                return overridden;
            }

            return nativeLayer;
        }

        /// <summary>
        /// Writes an NtsLayer to disk in GeoJSON FeatureCollection format (RFC 7946).
        /// Reconstructs attributes strictly from the layer schema, enforcing canonical casing, field ordering, and typed values.
        /// Automatically reprojects geometries to WGS84 (EPSG:4326) if the layer uses a different CRS.
        /// </summary>
        /// <param name="layer">The NtsLayer to export.</param>
        /// <param name="filePath">Target destination path (.geojson).</param>
        /// <returns name="success">True if export succeeded.</returns>
        public static bool WriteLayer(NtsLayer layer, string filePath)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            if (layer.Count == 0)
                throw new InvalidOperationException("Cannot write an empty layer to GeoJSON.");

            if (layer.CoordinateSystem == null)
            {
                throw new InvalidOperationException(
                    $"Cannot export layer '{layer.Name}' to GeoJSON because it has no Coordinate Reference System (CRS) defined. " +
                    "RFC 7946 requires WGS84 (EPSG:4326). Assign a CRS to the layer before exporting.");
            }

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            NtsLayer exportLayer = layer;
            if (!layer.CoordinateSystem.IsEquivalentTo(NtsCoordinateSystem.WGS84))
            {
                exportLayer = layer.Reproject(NtsCoordinateSystem.WGS84);
            }

            var cleanCollection = new FeatureCollection();
            var schemaFields = exportLayer.Schema.Fields;

            foreach (var feature in exportLayer.Features)
            {
                if (feature?.Geometry == null) continue;

                var table = new AttributesTable();
                foreach (var field in schemaFields)
                {
                    object? val = feature.GetValue(field.Name);
                    object? coerced = NtsAttribute.CoerceValue(val, field);
                    table.Add(field.Name, coerced);
                }

                cleanCollection.Add(new Feature(feature.Geometry.InternalGeometry, table));
            }

            var writer = new GeoJsonWriter();
            string json = writer.Write(cleanCollection);

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

        #region Internal Helpers

        private static NtsCoordinateSystem? ExtractEmbeddedCrs(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            int crsIndex = json.IndexOf("\"crs\"", StringComparison.OrdinalIgnoreCase);
            if (crsIndex < 0) return null;

            int length = Math.Min(350, json.Length - crsIndex);
            string crsSnippet = json.Substring(crsIndex, length);

            if (crsSnippet.IndexOf("CRS84", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return NtsCoordinateSystem.WGS84;
            }

            var match = Regex.Match(
                crsSnippet,
                @"(?:EPSG::?|""code""\s*:\s*|AUTHORITY\[\s*""EPSG""\s*,\s*""?)(?<epsg>\d+)",
                RegexOptions.IgnoreCase);

            if (match.Success && int.TryParse(match.Groups["epsg"].Value, out int epsg) && epsg > 0)
            {
                try
                {
                    return NtsCoordinateSystem.ByEPSG(epsg);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        #endregion
    }
}