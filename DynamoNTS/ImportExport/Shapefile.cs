using Autodesk.DesignScript.Runtime;
using Geometry;
using NetTopologySuite.Features;
using NetTopologySuite.IO.Esri;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImportExport
{
    /// <summary>
    /// Provides methods to import and export geometries using the ESRI Shapefile (.shp) format.
    /// </summary>
    public static class Shapefile
    {
        #region Create Node

        /// <summary>
        /// Reads all geometries from an ESRI Shapefile (.shp).
        /// </summary>
        /// <param name="filePath">Full file path to the .shp file.</param>
        /// <returns name="geometries">List of imported topological geometries.</returns>
        public static List<NtsGeometry> FromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("The file path cannot be null or whitespace.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"The shapefile was not found at: '{filePath}'");

            if (!Path.GetExtension(filePath).Equals(".shp", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The specified file must have a .shp extension.", nameof(filePath));

            // Reads geometry entities from the shapefile
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

        #endregion

        #region Action Node

        /// <summary>
        /// Writes a collection of NtsGeometries to an ESRI Shapefile (.shp).
        /// Note: An ESRI Shapefile must contain geometries of a uniform type (all points, all polylines, or all polygons).
        /// </summary>
        /// <param name="filePath">Target destination file path (.shp extension).</param>
        /// <param name="geometries">Collection of NtsGeometry entities to write.</param>
        /// <returns name="filePath">The file path of the generated shapefile.</returns>
        public static string ToFile(string filePath, IEnumerable<NtsGeometry> geometries)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("The destination path cannot be null or whitespace.", nameof(filePath));

            if (geometries == null)
                throw new ArgumentNullException(nameof(geometries));

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!Path.GetExtension(filePath).Equals(".shp", StringComparison.OrdinalIgnoreCase))
            {
                filePath = Path.ChangeExtension(filePath, ".shp");
            }

            var ntsList = geometries
                .Where(g => g != null && g.InternalGeometry != null && !g.InternalGeometry.IsEmpty)
                .Select(g => g.InternalGeometry)
                .ToList();

            if (ntsList.Count == 0)
                throw new InvalidOperationException("The geometry collection contains no valid geometries to export.");

            var features = new List<Feature>();
            int fid = 1;

            foreach (var geom in ntsList)
            {
                var attributes = new AttributesTable
                {
                    { "FID", fid++ }
                };
                features.Add(new Feature(geom, attributes));
            }

            NetTopologySuite.IO.Esri.Shapefile.WriteAllFeatures(features, filePath);

            return filePath;
        }

        #endregion
    }
}