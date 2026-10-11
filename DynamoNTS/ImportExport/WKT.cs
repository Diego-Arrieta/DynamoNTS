using Autodesk.DesignScript.Runtime;
using Geometry;
using NetTopologySuite.IO;
using System;

namespace ImportExport
{
    /// <summary>
    /// Provides methods to import and export geometries in Well-Known Text (WKT) format.
    /// </summary>
    public static class WKT
    {
        #region Actions

        /// <summary>
        /// Creates an NtsGeometry from a Well-Known Text (WKT) string.
        /// </summary>
        /// <param name="wkt">The WKT formatted string (e.g., 'POINT (30 10)', 'POLYGON ((...))').</param>
        /// <returns name="geometry">The imported topological geometry.</returns>
        public static NtsGeometry FromText(string wkt)
        {
            if (string.IsNullOrWhiteSpace(wkt))
                throw new ArgumentException("WKT string cannot be null or empty.", nameof(wkt));

            var reader = new WKTReader();
            var ntsGeometry = reader.Read(wkt);

            return NtsGeometry.WrapGeometry(ntsGeometry);
        }

        /// <summary>
        /// Converts an NtsGeometry into its Well-Known Text (WKT) string representation.
        /// </summary>
        /// <param name="geometry">The topological geometry to export.</param>
        /// <returns name="wkt">The WKT text representation.</returns>
        public static string ToText(NtsGeometry geometry)
        {
            if (geometry == null)
                throw new ArgumentNullException(nameof(geometry));

            var writer = new WKTWriter();
            return writer.Write(geometry.InternalGeometry);
        }

        #endregion
    }
}