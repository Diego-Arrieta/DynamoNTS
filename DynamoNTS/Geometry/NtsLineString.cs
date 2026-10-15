using Autodesk.DesignScript.Runtime;
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
        /// Creates an NtsLineString from a list of NtsPoints, preserving 3D coordinates if any point has elevation.
        /// </summary>
        /// <param name="points">List of topological NTS points.</param>
        /// <returns name="lineString">The equivalent topological line string in NTS.</returns>
        public static NtsLineString ByNtsPoints(IEnumerable<NtsPoint> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var ntsPoints = points.ToList();
            if (ntsPoints.Count < 2) throw new ArgumentException("A LineString requires at least 2 points.");

            bool is3D = ntsPoints.Any(p => p.HasZ);

            var coordinates = ntsPoints.Select(p => is3D
                ? (NtsGeometries.Coordinate)new NtsGeometries.CoordinateZ(p.X, p.Y, p.HasZ ? p.Z : 0.0)
                : new NtsGeometries.Coordinate(p.X, p.Y)
            ).ToArray();

            var coordList = new NtsGeometries.CoordinateList();
            coordList.Add(coordinates, false);
            var cleanCoords = coordList.ToCoordinateArray();

            if (cleanCoords.Length < 2) throw new ArgumentException("The provided points do not form a valid LineString after cleaning duplicates.");

            var lineString = NtsGeometries.GeometryFactory.Default.CreateLineString(cleanCoords);
            return new NtsLineString(lineString);
        }

        /// <summary>
        /// Creates an NtsLineString by extracting the vertices of a Dynamo PolyCurve,
        /// discretizing curved segments (such as arcs) according to a sagitta (chord height) tolerance.
        /// </summary>
        /// <param name="polyCurve">The base Dynamo PolyCurve.</param>
        /// <param name="keepZ">If true, preserves elevation (Z) from curves. Defaults to false (2D).</param>
        /// <param name="tolerance">Maximum allowable sagitta (chord height) when tessellating curves. Defaults to 0.01.</param>
        /// <returns name="lineString">The topological line string in NTS.</returns>
        public static NtsLineString ByPolyCurve(
            DynGeometry.PolyCurve polyCurve,
            [DefaultArgument("false")] bool keepZ = false,
            [DefaultArgument("0.01")] double tolerance = 0.01)
        {
            if (polyCurve == null) throw new ArgumentNullException(nameof(polyCurve));

            if (double.IsNaN(tolerance) || tolerance <= 0)
            {
                tolerance = 0.01;
            }

            var coordinates = new List<NtsGeometries.Coordinate>();
            var curves = polyCurve.Curves();

            foreach (var curve in curves)
            {
                if (curve is DynGeometry.Line)
                {
                    var start = curve.StartPoint;
                    AddCoordinate(coordinates, start, keepZ);
                    start.Dispose();
                }
                else if (curve is DynGeometry.Arc arc)
                {
                    int segments = CalculateArcSegments(arc.Radius, arc.SweepAngle, tolerance);
                    for (int i = 0; i < segments; i++)
                    {
                        double t = (double)i / segments;
                        var pt = arc.PointAtParameter(t);
                        AddCoordinate(coordinates, pt, keepZ);
                        pt.Dispose();
                    }
                }
                else if (curve is DynGeometry.Circle circle)
                {
                    int segments = Math.Max(8, CalculateArcSegments(circle.Radius, 360.0, tolerance));
                    for (int i = 0; i < segments; i++)
                    {
                        double t = (double)i / segments;
                        var pt = circle.PointAtParameter(t);
                        AddCoordinate(coordinates, pt, keepZ);
                        pt.Dispose();
                    }
                }
                else
                {
                    var p0 = curve.StartPoint;
                    var p1 = curve.EndPoint;
                    SampleCurveAdaptive(curve, 0.0, 1.0, p0, p1, tolerance, keepZ, coordinates, 0);
                    p0.Dispose();
                    p1.Dispose();
                }

                curve.Dispose();
            }

            var endPoint = polyCurve.EndPoint;
            AddCoordinate(coordinates, endPoint, keepZ);
            endPoint.Dispose();

            var coordList = new NtsGeometries.CoordinateList();
            coordList.Add(coordinates.ToArray(), false);
            var cleanCoords = coordList.ToCoordinateArray();

            if (cleanCoords.Length < 2) throw new ArgumentException("PolyCurve must have at least 2 distinct vertices.");

            var lineString = NtsGeometries.GeometryFactory.Default.CreateLineString(cleanCoords);
            return new NtsLineString(lineString);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Converts the NtsLineString back into the Dynamo visual environment as a PolyCurve.
        /// </summary>
        /// <returns name="polyCurve">A native Dynamo PolyCurve.</returns>
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

        #region Internal Discretization Helpers

        private static int CalculateArcSegments(double radius, double sweepAngleDegrees, double tolerance)
        {
            if (radius <= 1e-6) return 1;

            double sweepRad = Math.Abs(sweepAngleDegrees) * (Math.PI / 180.0);
            if (sweepRad <= 1e-6) return 1;

            double ratio = tolerance / radius;
            double cosHalf = Math.Max(-1.0, Math.Min(1.0, 1.0 - ratio));
            double dTheta = 2.0 * Math.Acos(cosHalf);

            if (dTheta <= 1e-6) return 1;

            int segments = (int)Math.Ceiling(sweepRad / dTheta);
            return Math.Max(1, Math.Min(segments, 1000));
        }

        private static void SampleCurveAdaptive(
            DynGeometry.Curve curve,
            double t0,
            double t1,
            DynGeometry.Point p0,
            DynGeometry.Point p1,
            double tolerance,
            bool keepZ,
            List<NtsGeometries.Coordinate> coordinates,
            int depth)
        {
            double tm = (t0 + t1) * 0.5;
            var pm = curve.PointAtParameter(tm);

            double sagitta = DistancePointToSegment(pm, p0, p1);

            if (sagitta > tolerance && depth < 8)
            {
                SampleCurveAdaptive(curve, t0, tm, p0, pm, tolerance, keepZ, coordinates, depth + 1);
                SampleCurveAdaptive(curve, tm, t1, pm, p1, tolerance, keepZ, coordinates, depth + 1);
            }
            else
            {
                AddCoordinate(coordinates, p0, keepZ);
            }

            pm.Dispose();
        }

        private static double DistancePointToSegment(DynGeometry.Point p, DynGeometry.Point a, DynGeometry.Point b)
        {
            double abx = b.X - a.X;
            double aby = b.Y - a.Y;
            double abz = b.Z - a.Z;

            double apx = p.X - a.X;
            double apy = p.Y - a.Y;
            double apz = p.Z - a.Z;

            double abLenSq = abx * abx + aby * aby + abz * abz;
            if (abLenSq < 1e-12)
            {
                return Math.Sqrt(apx * apx + apy * apy + apz * apz);
            }

            double t = (apx * abx + apy * aby + apz * abz) / abLenSq;
            t = Math.Max(0.0, Math.Min(1.0, t));

            double projX = a.X + t * abx;
            double projY = a.Y + t * aby;
            double projZ = a.Z + t * abz;

            double dx = p.X - projX;
            double dy = p.Y - projY;
            double dz = p.Z - projZ;

            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static void AddCoordinate(List<NtsGeometries.Coordinate> coordinates, DynGeometry.Point pt, bool keepZ)
        {
            if (keepZ)
                coordinates.Add(new NtsGeometries.CoordinateZ(pt.X, pt.Y, pt.Z));
            else
                coordinates.Add(new NtsGeometries.Coordinate(pt.X, pt.Y));
        }

        #endregion
    }
}