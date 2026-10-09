using Autodesk.DesignScript.Runtime;
using NetTopologySuite.Algorithm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometry = NetTopologySuite.Geometries;

namespace Geometry
{
    /// <summary>
    /// Represents a topological polygon from NetTopologySuite.
    /// </summary>
    public class NtsPolygon
    {
        /// <summary>
        /// Internal NTS geometry. Hidden from the Dynamo UI.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometry.Polygon InternalPolygon { get; set; }

        /// <summary>
        /// Internal constructor. Used only from within the C# codebase.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsPolygon(NtsGeometry.Polygon polygon)
        {
            InternalPolygon = polygon ?? throw new ArgumentNullException(nameof(polygon));
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsPolygon by extracting the exterior boundary of a Dynamo Polygon.
        /// </summary>
        /// <param name="polygon">The base Dynamo Polygon.</param>
        /// <returns name="NtsPolygon">The equivalent topological polygon in NTS.</returns>
        public static NtsPolygon ByDynamoPolygon(DynGeometry.Polygon polygon)
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            var points = polygon.Points;
            var coordinates = new List<NtsGeometry.CoordinateZ>();

            // Extract vertices and manage unmanaged Dynamo memory
            foreach (var pt in points)
            {
                coordinates.Add(new NtsGeometry.CoordinateZ(pt.X, pt.Y, pt.Z));
                pt.Dispose(); // Crucial to prevent RAM leaks
            }

            // NTS requires polygons to be explicitly closed rings (StartPoint == EndPoint)
            if (coordinates.Count > 0)
            {
                var first = coordinates[0];
                var last = coordinates[coordinates.Count - 1];

                // Check if the polygon is already closed; if not, add the closing point
                if (first.X != last.X || first.Y != last.Y || first.Z != last.Z)
                {
                    coordinates.Add(new NtsGeometry.CoordinateZ(first.X, first.Y, first.Z));
                }
            }

            // A valid NTS polygon ring requires at least 4 coordinates (e.g., Triangle = 3 points + 1 closing point)
            if (coordinates.Count < 4)
                throw new ArgumentException("A valid polygon requires at least 3 distinct vertices.");

            var ntsPolygon = NtsGeometry.GeometryFactory.Default.CreatePolygon(coordinates.ToArray());
            return new NtsPolygon(ntsPolygon);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Converts the NtsPolygon back into the Dynamo visual environment.
        /// Notes: Dynamo Polygon.ByPoints expects distinct points and closes the shape automatically.
        /// </summary>
        /// <returns name="Polygon">A native Dynamo Polygon.</returns>
        public DynGeometry.Polygon ToDynamoPolygon()
        {
            var dynamoPoints = new List<DynGeometry.Point>();

            // Extract only the exterior ring (ignoring inner holes for standard Dynamo Polygon)
            var coords = InternalPolygon.ExteriorRing.Coordinates;

            // NTS rings are closed (first == last), but Dynamo expects unclosed point lists
            // So we iterate up to coords.Length - 1 to drop the duplicated closing point
            for (int i = 0; i < coords.Length - 1; i++)
            {
                var coord = coords[i];

                // Protect against 2D planar boolean results returning NaN elevation
                double z = double.IsNaN(coord.Z) ? 0.0 : coord.Z;

                dynamoPoints.Add(DynGeometry.Point.ByCoordinates(coord.X, coord.Y, z));
            }

            var dynPolygon = DynGeometry.Polygon.ByPoints(dynamoPoints);

            // Dispose intermediate points to free up Dynamo rendering memory
            foreach (var pt in dynamoPoints)
            {
                pt.Dispose();
            }

            return dynPolygon;
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Gets the geometric area of the polygon.
        /// </summary>
        public double Area => InternalPolygon.Area;

        /// <summary>
        /// Gets the perimeter length of the exterior boundary.
        /// </summary>
        public double Perimeter => InternalPolygon.Length;

        /// <summary>
        /// Calculates the mathematical centroid (center of mass) of the polygon.
        /// </summary>
        public NtsPoint Centroid => new NtsPoint(InternalPolygon.Centroid);

        #endregion
    }
}