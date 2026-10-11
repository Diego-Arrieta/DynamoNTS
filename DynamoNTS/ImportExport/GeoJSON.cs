using Autodesk.DesignScript.Runtime;
using Geometry;
using NetTopologySuite.IO;
using System;
using NtsGeometries = NetTopologySuite.Geometries;

namespace ImportExport
{
    /// <summary>
    /// Provides methods to import and export geometries in GeoJSON format.
    /// </summary>
    public static class GeoJSON
    {
        #region Actions

        /// <summary>
        /// Creates an NtsGeometry from a GeoJSON geometry string.
        /// </summary>
        /// <param name="geoJson">The GeoJSON formatted string representing a geometry or feature.</param>
        /// <returns name="geometry">The imported topological geometry.</returns>
        public static NtsGeometry FromText(string geoJson)
        {
            if (string.IsNullOrWhiteSpace(geoJson))
                throw new ArgumentException("GeoJSON string cannot be null or empty.", nameof(geoJson));

            var reader = new GeoJsonReader();
            var ntsGeometry = reader.Read<NtsGeometries.Geometry>(geoJson);

            return NtsGeometry.WrapGeometry(ntsGeometry);
        }

        /// <summary>
        /// Converts an NtsGeometry into its GeoJSON string representation.
        /// </summary>
        /// <param name="geometry">The topological geometry to export.</param>
        /// <returns name="geoJson">The GeoJSON text representation.</returns>
        public static string ToText(NtsGeometry geometry)
        {
            if (geometry == null)
                throw new ArgumentNullException(nameof(geometry));

            var writer = new GeoJsonWriter();
            return writer.Write(geometry.InternalGeometry);
        }

        #endregion
    }
}