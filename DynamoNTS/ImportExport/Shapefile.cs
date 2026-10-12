using Autodesk.DesignScript.Runtime;
using DynamoNTS.Features;
using DynamoNTS.Geometry;
using DynamoNTS.Layers;
using DynamoNTS.Projections;
using NetTopologySuite.Features;
using NetTopologySuite.IO.Esri;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DynamoNTS.ImportExport
{
    /// <summary>
    /// Provides methods to import and export geometries, features, and layers using the ESRI Shapefile (.shp) format.
    /// </summary>
    public static class Shapefile
    {
        #region Read Nodes

        /// <summary>
        /// Reads all geometries from an ESRI Shapefile (.shp), ignoring database attributes.
        /// </summary>
        /// <param name="filePath">Full file path to the .shp file.</param>
        /// <returns name="geometries">List of imported topological geometries.</returns>
        public static List<NtsGeometry> FromFile(string filePath)
        {
            ValidatePathForRead(filePath);

            var ntsGeometries = NetTopologySuite.IO.Esri.Shapefile.ReadAllGeometries(filePath);
            var result = new List<NtsGeometry>();

            foreach (var geom in ntsGeometries)
            {
                var wrapped = NtsGeometry.WrapGeometry(geom);
                if (wrapped != null)
                {
                    result.Add(wrapped);
                }
            }

            return result;
        }

        /// <summary>
        /// Reads all features (geometries and .dbf attribute tables) from an ESRI Shapefile.
        /// </summary>
        /// <param name="filePath">Full file path to the .shp file.</param>
        /// <returns name="features">List of imported NtsFeature entities.</returns>
        public static List<NtsFeature> ReadFeatures(string filePath)
        {
            ValidatePathForRead(filePath);

            var rawFeatures = NetTopologySuite.IO.Esri.Shapefile.ReadAllFeatures(filePath);
            var result = new List<NtsFeature>();

            foreach (var rawFeature in rawFeatures)
            {
                var feature = NtsFeature.FromNtsFeature(rawFeature);
                if (feature != null)
                {
                    result.Add(feature);
                }
            }

            return result;
        }

        /// <summary>
        /// Reads an ESRI Shapefile into a complete NtsLayer, detecting associated projection (.prj) automatically if present.
        /// </summary>
        /// <param name="filePath">Full file path to the .shp file.</param>
        /// <param name="overrideCRS">Optional coordinate system override. If null, attempts to read from the .prj file.</param>
        /// <returns name="layer">The imported NtsLayer.</returns>
        public static NtsLayer ReadLayer(string filePath, CoordinateSystem overrideCRS = null)
        {
            ValidatePathForRead(filePath);

            var features = ReadFeatures(filePath);
            string layerName = Path.GetFileNameWithoutExtension(filePath);

            CoordinateSystem crs = overrideCRS;
            if (crs == null)
            {
                string prjPath = Path.ChangeExtension(filePath, ".prj");
                if (File.Exists(prjPath))
                {
                    try
                    {
                        crs = CoordinateSystem.ByPrjFile(prjPath);
                    }
                    catch
                    {
                        // Fallback if .prj cannot be parsed
                        crs = null;
                    }
                }
            }

            return NtsLayer.ByFeatures(features, layerName, crs);
        }

        #endregion

        #region Write Nodes

        /// <summary>
        /// Writes an NtsGeometry collection to an ESRI Shapefile (.shp) with a default incremental FID.
        /// </summary>
        /// <param name="filePath">Target destination file path (.shp extension).</param>
        /// <param name="geometries">Collection of NtsGeometry entities to export.</param>
        /// <returns name="filePath">The file path of the generated shapefile.</returns>
        public static string ToFile(string filePath, IEnumerable<NtsGeometry> geometries)
        {
            if (geometries == null)
                throw new ArgumentNullException(nameof(geometries));

            filePath = PrepareDestinationPath(filePath);

            var validGeometries = geometries
                .Where(g => g?.InternalGeometry != null && !g.InternalGeometry.IsEmpty)
                .ToList();

            if (validGeometries.Count == 0)
                throw new InvalidOperationException("The geometry collection contains no valid geometries to export.");

            var features = new List<Feature>();
            int fid = 1;

            foreach (var geom in validGeometries)
            {
                var attributes = new AttributesTable
                {
                    { "FID", fid++ }
                };
                features.Add(new Feature(geom.InternalGeometry, attributes));
            }

            NetTopologySuite.IO.Esri.Shapefile.WriteAllFeatures(features, filePath);

            return filePath;
        }

        /// <summary>
        /// Writes a collection of NtsFeature entities to an ESRI Shapefile (.shp, .shx, and .dbf).
        /// </summary>
        /// <param name="filePath">Target destination file path (.shp extension).</param>
        /// <param name="features">Collection of NtsFeature entities.</param>
        /// <returns name="filePath">The file path of the generated shapefile.</returns>
        public static string WriteFeatures(string filePath, IEnumerable<NtsFeature> features)
        {
            if (features == null)
                throw new ArgumentNullException(nameof(features));

            filePath = PrepareDestinationPath(filePath);

            var validFeatures = features
                .Where(f => f?.Geometry?.InternalGeometry != null && !f.Geometry.InternalGeometry.IsEmpty)
                .Select(f => f.InternalFeature)
                .ToList();

            if (validFeatures.Count == 0)
                throw new InvalidOperationException("The feature collection contains no valid geometries to export.");

            NetTopologySuite.IO.Esri.Shapefile.WriteAllFeatures(validFeatures, filePath);

            return filePath;
        }

        /// <summary>
        /// Writes an entire NtsLayer to an ESRI Shapefile, generating .shp, .shx, .dbf, and the projection file (.prj) if CRS is defined.
        /// </summary>
        /// <param name="filePath">Target destination file path (.shp extension).</param>
        /// <param name="layer">The NtsLayer to export.</param>
        /// <returns name="filePath">The file path of the generated shapefile.</returns>
        public static string WriteLayer(string filePath, NtsLayer layer)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));

            string savedPath = WriteFeatures(filePath, layer.Features);

            if (layer.CoordinateSystem?.InternalCRS != null)
            {
                try
                {
                    string prjPath = Path.ChangeExtension(savedPath, ".prj");
                    string wkt = layer.CoordinateSystem.InternalCRS.WKT;
                    if (!string.IsNullOrWhiteSpace(wkt))
                    {
                        File.WriteAllText(prjPath, wkt);
                    }
                }
                catch
                {
                    // Fail silently for .prj generation to not block main export
                }
            }

            return savedPath;
        }

        #endregion

        #region Helper Methods

        private static void ValidatePathForRead(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("The file path cannot be null or whitespace.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"The shapefile was not found at: '{filePath}'");

            if (!Path.GetExtension(filePath).Equals(".shp", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The specified file must have a .shp extension.", nameof(filePath));
        }

        private static string PrepareDestinationPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("The destination path cannot be null or whitespace.", nameof(filePath));

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!Path.GetExtension(filePath).Equals(".shp", StringComparison.OrdinalIgnoreCase))
            {
                filePath = Path.ChangeExtension(filePath, ".shp");
            }

            return filePath;
        }

        #endregion
    }
}