using Autodesk.DesignScript.Runtime;
using NetTopologySuite.Algorithm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometries = NetTopologySuite.Geometries;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Represents a topological polygon from NetTopologySuite.
    /// </summary>
    public class NtsPolygon : NtsGeometry
    {
        /// <summary>
        /// Internal NTS geometry. Hidden from the Dynamo UI.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.Polygon InternalPolygon
        {
            get => (NtsGeometries.Polygon)InternalGeometry;
            set => InternalGeometry = value;
        }

        /// <summary>
        /// Internal constructor. Used only from within the C# codebase.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsPolygon(NtsGeometries.Polygon polygon) : base(polygon)
        {
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsPolygon by extracting the exterior boundary of a Dynamo Polygon.
        /// </summary>
        /// <param name="polygon">The base Dynamo Polygon.</param>
        /// <returns name="NtsPolygon">The equivalent topological polygon in NTS.</returns>
        public static NtsPolygon ByPolygon(DynGeometry.Polygon polygon)
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            var points = polygon.Points;
            var coordinates = new List<NtsGeometries.CoordinateZ>();

            foreach (var pt in points)
            {
                coordinates.Add(new NtsGeometries.CoordinateZ(pt.X, pt.Y, pt.Z));
                pt.Dispose();
            }

            if (coordinates.Count > 0)
            {
                var first = coordinates[0];
                var last = coordinates[coordinates.Count - 1];

                if (first.X != last.X || first.Y != last.Y || first.Z != last.Z)
                {
                    coordinates.Add(new NtsGeometries.CoordinateZ(first.X, first.Y, first.Z));
                }
            }

            if (coordinates.Count < 4)
                throw new ArgumentException("A valid polygon requires at least 3 distinct vertices.");

            var ntsPolygon = NtsGeometries.GeometryFactory.Default.CreatePolygon(coordinates.ToArray());
            return new NtsPolygon(ntsPolygon);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Converts the NtsPolygon back into the Dynamo visual environment.
        /// Notes: Dynamo Polygon.ByPoints expects distinct points and closes the shape automatically.
        /// </summary>
        /// <returns name="Polygon">A native Dynamo Polygon.</returns>
        public DynGeometry.Polygon ToPolygon()
        {
            var dynamoPoints = new List<DynGeometry.Point>();

            var coords = InternalPolygon.ExteriorRing.Coordinates;

            for (int i = 0; i < coords.Length - 1; i++)
            {
                var coord = coords[i];

                double z = double.IsNaN(coord.Z) ? 0.0 : coord.Z;

                dynamoPoints.Add(DynGeometry.Point.ByCoordinates(coord.X, coord.Y, z));
            }

            var dynPolygon = DynGeometry.Polygon.ByPoints(dynamoPoints);

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