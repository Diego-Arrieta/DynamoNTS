using Autodesk.DesignScript.Runtime;
using NetTopologySuite.Algorithm;
using System;
using System.Collections.Generic;
using System.Linq;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometries = NetTopologySuite.Geometries;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Represents a topological line string (polyline) from NetTopologySuite.
    /// </summary>
    public class NtsLineString : NtsGeometry
    {
        /// <summary>
        /// Internal NTS geometry.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.LineString InternalLineString
        {
            get => (NtsGeometries.LineString)InternalGeometry;
            set => InternalGeometry = value;
        }

        /// <summary>
        /// Internal constructor.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsLineString(NtsGeometries.LineString lineString) : base(lineString)
        {
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

            var coordinates = ntsPoints.Select(p => new NtsGeometries.CoordinateZ(p.X, p.Y, p.Z)).ToArray();

            var cleanCoords = new NtsGeometries.CoordinateList(coordinates).ToCoordinateArray();

            if (cleanCoords.Length < 2) throw new ArgumentException("The provided points do not form a valid LineString after cleaning duplicates.");

            var lineString = NtsGeometries.GeometryFactory.Default.CreateLineString(cleanCoords);
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

            var coordinates = new List<NtsGeometries.Coordinate>();
            var curves = polyCurve.Curves();

            foreach (var curve in curves)
            {
                var start = curve.StartPoint;
                coordinates.Add(new NtsGeometries.CoordinateZ(start.X, start.Y, start.Z));

                start.Dispose();
                curve.Dispose();
            }

            var endPoint = polyCurve.EndPoint;
            coordinates.Add(new NtsGeometries.CoordinateZ(endPoint.X, endPoint.Y, endPoint.Z));
            endPoint.Dispose();

            var cleanCoords = new NtsGeometries.CoordinateList(coordinates.ToArray()).ToCoordinateArray();

            if (cleanCoords.Length < 2) throw new ArgumentException("PolyCurve must have at least 2 distinct vertices.");

            var lineString = NtsGeometries.GeometryFactory.Default.CreateLineString(cleanCoords);
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
            var points = new List<DynGeometry.Point>();

            foreach (var coord in InternalLineString.Coordinates)
            {
                double z = double.IsNaN(coord.Z) ? 0.0 : coord.Z;
                points.Add(DynGeometry.Point.ByCoordinates(coord.X, coord.Y, z));
            }

            var polyCurve = DynGeometry.PolyCurve.ByPoints(points);

            foreach (var pt in points)
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