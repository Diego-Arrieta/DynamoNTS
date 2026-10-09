using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.DesignScript.Runtime;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometry = NetTopologySuite.Geometries;

namespace Geometry
{
    /// <summary>
    /// Represents a topological line string (polyline) from NetTopologySuite.
    /// </summary>
    public class NtsLineString
    {
        /// <summary>
        /// Internal NTS geometry.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometry.LineString InternalLineString { get; set; }

        /// <summary>
        /// Internal constructor.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsLineString(NtsGeometry.LineString lineString)
        {
            InternalLineString = lineString ?? throw new ArgumentNullException(nameof(lineString));
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsLineString from a list of NtsPoints.
        /// </summary>
        /// <param name="points">List of topological NTS points.</param>
        /// <returns name="NtsLineString">The equivalent topological line string in NTS.</returns>
        public static NtsLineString ByNtsPoints(IEnumerable<NtsPoint> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var ntsPoints = points.ToList();
            if (ntsPoints.Count < 2) throw new ArgumentException("A LineString requires at least 2 points.");

            var coordinates = ntsPoints.Select(p => new NtsGeometry.CoordinateZ(p.X, p.Y, p.Z)).ToArray();

            var cleanCoords = new NtsGeometry.CoordinateList(coordinates).ToCoordinateArray();

            if (cleanCoords.Length < 2) throw new ArgumentException("The provided points do not form a valid LineString after cleaning duplicates.");

            var lineString = NtsGeometry.GeometryFactory.Default.CreateLineString(cleanCoords);
            return new NtsLineString(lineString);
        }

        /// <summary>
        /// Creates an NtsLineString by extracting the vertices of a Dynamo PolyCurve.
        /// </summary>
        /// <param name="polyCurve">The base Dynamo PolyCurve.</param>
        /// <returns name="NtsLineString">The topological line string in NTS.</returns>
        public static NtsLineString ByPolyCurve(DynGeometry.PolyCurve polyCurve)
        {
            if (polyCurve == null) throw new ArgumentNullException(nameof(polyCurve));

            var coordinates = new List<NtsGeometry.Coordinate>();
            var curves = polyCurve.Curves();

            // Extract points along the curve while managing unmanaged memory (Dispose)
            foreach (var curve in curves)
            {
                var start = curve.StartPoint;
                coordinates.Add(new NtsGeometry.CoordinateZ(start.X, start.Y, start.Z));

                // Crucial for Dynamo performance in Civil 3D
                start.Dispose();
                curve.Dispose();
            }

            // Add the absolute end point
            var endPoint = polyCurve.EndPoint;
            coordinates.Add(new NtsGeometry.CoordinateZ(endPoint.X, endPoint.Y, endPoint.Z));
            endPoint.Dispose();

            // Remove consecutive duplicates
            var cleanCoords = new NtsGeometry.CoordinateList(coordinates.ToArray()).ToCoordinateArray();

            if (cleanCoords.Length < 2) throw new ArgumentException("PolyCurve must have at least 2 distinct vertices.");

            var lineString = NtsGeometry.GeometryFactory.Default.CreateLineString(cleanCoords);
            return new NtsLineString(lineString);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Converts the NtsLineString back into the Dynamo visual environment as a PolyCurve.
        /// </summary>
        /// <returns name="PolyCurve">A native Dynamo PolyCurve.</returns>
        public DynGeometry.PolyCurve ToPolyCurve()
        {
            var dynamoPoints = new List<DynGeometry.Point>();

            foreach (var coord in InternalLineString.Coordinates)
            {
                // Protect against 2D planar results returning NaN elevation
                double z = double.IsNaN(coord.Z) ? 0.0 : coord.Z;
                dynamoPoints.Add(DynGeometry.Point.ByCoordinates(coord.X, coord.Y, z));
            }

            var polyCurve = DynGeometry.PolyCurve.ByPoints(dynamoPoints);

            // Dispose intermediate points to free up Dynamo rendering memory
            foreach (var pt in dynamoPoints)
            {
                pt.Dispose();
            }

            return polyCurve;
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Gets the total length of the LineString.
        /// </summary>
        public double Length => InternalLineString.Length;

        /// <summary>
        /// Checks if the LineString is closed (Start Point equals End Point).
        /// </summary>
        public bool IsClosed => InternalLineString.IsClosed;

        /// <summary>
        /// Gets the starting point of the LineString.
        /// </summary>
        public NtsPoint StartPoint => new NtsPoint(InternalLineString.StartPoint);

        /// <summary>
        /// Gets the ending point of the LineString.
        /// </summary>
        public NtsPoint EndPoint => new NtsPoint(InternalLineString.EndPoint);

        #endregion
    }
}