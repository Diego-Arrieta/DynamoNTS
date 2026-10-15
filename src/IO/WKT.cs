using Autodesk.DesignScript.Runtime;
using DynamoNTS.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System;
using NtsGeom = NetTopologySuite.Geometries.Geometry;

namespace DynamoNTS.IO
{
    /// <summary>
    /// Provides methods to import and export geometries using OGC Well-Known Text (WKT).
    /// </summary>
    public static class WKT
    {
        #region Action Nodes

        /// <summary>
        /// Creates an NtsGeometry from an OGC Well-Known Text (WKT) string representation.
        /// Accurately parses 3D geometries (Z) supporting both modern ISO syntax (POINT Z) and legacy 3D CAD/GIS syntax.
        /// </summary>
        /// <param name="wktString">The Well-Known Text (WKT) string.</param>
        /// <returns name="geometry">The imported NtsGeometry, or null if parsing fails.</returns>
        public static NtsGeometry? FromText(string wktString)
        {
            if (string.IsNullOrWhiteSpace(wktString))
                throw new ArgumentException("WKT string cannot be null or empty.", nameof(wktString));

            var reader = new WKTReader
            {
                IsOldNtsCoordinateSyntaxAllowed = true,
            };

            var ntsGeom = reader.Read(wktString);
            return NtsGeometry.WrapGeometry(ntsGeom);
        }

        /// <summary>
        /// Converts an NtsGeometry into its OGC Well-Known Text (WKT) string representation.
        /// Preserves 3D coordinates (Z) when present in the geometry.
        /// </summary>
        /// <param name="geometry">The topological geometry to serialize.</param>
        /// <returns name="wkt">The Well-Known Text string representation.</returns>
        public static string ToText(NtsGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            // B38: Preservar dimensión Z en la serialización si la geometría es 3D
            bool is3D = geometry.HasZ;
            var writer = new WKTWriter(is3D ? 3 : 2)
            {
                OutputOrdinates = is3D ? Ordinates.XYZ : Ordinates.XY
            };

            return writer.Write(geometry.InternalGeometry);
        }

        #endregion
    }
}