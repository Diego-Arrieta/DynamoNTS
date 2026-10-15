using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometries = NetTopologySuite.Geometries;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Represents a topological polygon from NetTopologySuite, supporting exterior boundary and interior holes.
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
        /// Internal constructor.
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
        /// <param name="keepZ">Indicates whether Z coordinates are preserved.</param>
        /// <returns name="polygon">The topological polygon in NTS.</returns>
        public static NtsPolygon ByPolygon(
            DynGeometry.Polygon polygon,
            [DefaultArgument("false")] bool keepZ = false)
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            var points = polygon.Points;
            var coords = points.Select(pt => keepZ
                ? (NtsGeometries.Coordinate)new NtsGeometries.CoordinateZ(pt.X, pt.Y, pt.Z)
                : new NtsGeometries.Coordinate(pt.X, pt.Y)
            ).ToArray();

            foreach (var pt in points)
            {
                pt.Dispose();
            }

            var shell = ToLinearRing(coords);
            var ntsPolygon = NtsGeometries.GeometryFactory.Default.CreatePolygon(shell);

            return new NtsPolygon(ntsPolygon);
        }

        /// <summary>
        /// Creates an NtsPolygon defined by an exterior boundary ring and optional interior hole rings.
        /// </summary>
        /// <param name="exteriorRing">The outer boundary LineString.</param>
        /// <param name="interiorRings">Optional collection of LineStrings representing holes/voids.</param>
        /// <returns name="polygon">The topological polygon with holes.</returns>
        public static NtsPolygon ByRings(
            NtsLineString exteriorRing,
            [DefaultArgument("null")] IList<NtsLineString>? interiorRings = null)
        {
            if (exteriorRing == null) throw new ArgumentNullException(nameof(exteriorRing));

            var shell = ToLinearRing(exteriorRing.InternalLineString.Coordinates);

            NtsGeometries.LinearRing[]? holes = null;
            if (interiorRings != null && interiorRings.Count > 0)
            {
                holes = interiorRings
                    .Where(h => h != null && !h.InternalGeometry.IsEmpty)
                    .Select(h => ToLinearRing(h.InternalLineString.Coordinates))
                    .ToArray();
            }

            var ntsPolygon = NtsGeometries.GeometryFactory.Default.CreatePolygon(shell, holes);
            return new NtsPolygon(ntsPolygon);
        }

        /// <summary>
        /// Creates an NtsPolygon from closed Dynamo PolyCurves (exterior boundary and optional holes).
        /// </summary>
        /// <param name="exteriorCurve">The closed outer boundary PolyCurve.</param>
        /// <param name="interiorCurves">Optional list of closed PolyCurves representing holes.</param>
        /// <param name="keepZ">If true, preserves elevation (Z) from curves. Defaults to false (2D).</param>
        /// <returns name="polygon">The topological polygon with holes.</returns>
        public static NtsPolygon ByPolyCurves(
            DynGeometry.PolyCurve exteriorCurve,
            [DefaultArgument("null")] IList<DynGeometry.PolyCurve>? interiorCurves = null,
            [DefaultArgument("false")] bool keepZ = false)
        {
            if (exteriorCurve == null) throw new ArgumentNullException(nameof(exteriorCurve));

            var extLine = NtsLineString.ByPolyCurve(exteriorCurve, keepZ);
            var holeLines = interiorCurves?
                .Where(c => c != null)
                .Select(c => NtsLineString.ByPolyCurve(c, keepZ))
                .ToList();

            return ByRings(extLine, holeLines);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Converts the exterior boundary into a native Dynamo Polygon (holes are not represented).
        /// </summary>
        /// <returns name="polygon">The outer boundary as a Dynamo Polygon.</returns>
        public DynGeometry.Polygon ToPolygon()
        {
            var coords = InternalPolygon.ExteriorRing.Coordinates;
            var dynamoPoints = new List<DynGeometry.Point>();

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

        /// <summary>
        /// Converts all polygon rings (exterior shell and any interior holes) into native Dynamo PolyCurves.
        /// Index 0 is always the exterior ring; subsequent elements are interior holes.
        /// </summary>
        /// <returns name="polyCurves">List of PolyCurves representing all boundaries.</returns>
        public List<DynGeometry.PolyCurve> ToPolyCurves()
        {
            var curves = new List<DynGeometry.PolyCurve> { ExteriorRing.ToPolyCurve() };
            foreach (var hole in Holes)
            {
                curves.Add(hole.ToPolyCurve());
            }
            return curves;
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Gets the net geometric area (exterior area minus all holes).
        /// </summary>
        public double Area => InternalPolygon.Area;

        /// <summary>
        /// Gets the total perimeter including the exterior boundary and all interior holes.
        /// </summary>
        public double Perimeter => InternalPolygon.Length;

        /// <summary>
        /// Gets the perimeter of the exterior ring only.
        /// </summary>
        public double ExteriorPerimeter => InternalPolygon.ExteriorRing.Length;

        /// <summary>
        /// Calculates the mathematical centroid (center of mass).
        /// Note: For concave shapes or polygons with central holes, this point may fall outside the solid region.
        /// </summary>
        public NtsPoint Centroid => new NtsPoint(InternalPolygon.Centroid);

        /// <summary>
        /// Computes a point guaranteed to lie strictly inside the polygon interior (outside of any holes).
        /// Ideal for placing labels, inspection points, or property tags.
        /// </summary>
        public NtsPoint PointOnSurface => new NtsPoint(InternalPolygon.InteriorPoint);

        /// <summary>
        /// Gets the outer boundary as an NtsLineString.
        /// </summary>
        public NtsLineString ExteriorRing => new NtsLineString(InternalPolygon.ExteriorRing);

        /// <summary>
        /// Gets all interior void rings (holes) as NtsLineStrings.
        /// </summary>
        public List<NtsLineString> Holes
        {
            get
            {
                var list = new List<NtsLineString>();
                for (int i = 0; i < InternalPolygon.NumInteriorRings; i++)
                {
                    list.Add(new NtsLineString(InternalPolygon.GetInteriorRingN(i)));
                }
                return list;
            }
        }

        /// <summary>
        /// Gets the total number of interior holes.
        /// </summary>
        public int HoleCount => InternalPolygon.NumInteriorRings;

        /// <summary>
        /// Checks whether the polygon contains one or more interior holes.
        /// </summary>
        public bool HasHoles => InternalPolygon.NumInteriorRings > 0;

        #endregion

        #region Internal Helpers

        private static NtsGeometries.LinearRing ToLinearRing(NtsGeometries.Coordinate[] coordinates)
        {
            if (coordinates == null || coordinates.Length < 3)
                throw new ArgumentException("A ring requires at least 3 distinct coordinates.");

            var coordsList = new List<NtsGeometries.Coordinate>(coordinates);

            var first = coordsList[0];
            var last = coordsList[coordsList.Count - 1];

            if (!first.Equals2D(last))
            {
                coordsList.Add(first.Copy());
            }

            if (coordsList.Count < 4)
                throw new ArgumentException("A closed ring requires at least 4 coordinates (3 distinct vertices + closing point).");

            return NtsGeometries.GeometryFactory.Default.CreateLinearRing(coordsList.ToArray());
        }

        #endregion
    }
}