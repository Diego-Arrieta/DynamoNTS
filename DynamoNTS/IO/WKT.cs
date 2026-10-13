using Autodesk.DesignScript.Runtime;
using DynamoNTS.Geometry;
using NetTopologySuite.IO;
using System;
using NtsGeom = NetTopologySuite.Geometries.Geometry;

namespace DynamoNTS.IO
{
    /// <summary>
    /// Provides methods to serialize and deserialize geometries using the OGC Well-Known Text (WKT) standard.
    /// </summary>
    public static class WKT
    {
        #region Action Nodes

        /// <summary>
        /// Parses an OGC Well-Known Text (WKT) string into an NtsGeometry.
        /// </summary>
        /// <param name="wktString">The WKT geometry string (e.g., 'POLYGON ((0 0, 10 0, 10 10, 0 0))').</param>
        /// <returns name="geometry">The imported topological geometry.</returns>
        public static NtsGeometry FromText(string wktString)
        {
            if (string.IsNullOrWhiteSpace(wktString))
                throw new ArgumentException("WKT string cannot be null or empty.", nameof(wktString));

            var reader = new WKTReader();
            var ntsGeometry = reader.Read(wktString);

            return NtsGeometry.WrapGeometry(ntsGeometry);
        }

        /// <summary>
        /// Converts an NtsGeometry into its standard OGC Well-Known Text (WKT) string representation.
        /// </summary>
        /// <param name="geometry">The topological geometry to export.</param>
        /// <returns name="wktString">The WKT string representation.</returns>
        public static string ToText(NtsGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var writer = new WKTWriter();
            return writer.Write(geometry.InternalGeometry);
        }

        #endregion
    }
}