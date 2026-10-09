using System;
using Autodesk.DesignScript.Runtime;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometry = NetTopologySuite.Geometries;

namespace Geometry
{
    /// <summary>
    /// Represents a topological point from NetTopologySuite.
    /// </summary>
    public class NtsPoint
    {
        /// <summary>
        /// Internal NTS geometry.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometry.Point InternalPoint { get; set; }

        /// <summary>
        /// Internal constructor.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsPoint(NtsGeometry.Point point)
        {
            InternalPoint = point ?? throw new ArgumentNullException(nameof(point));
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsPoint from a native Dynamo Point.
        /// </summary>
        /// <param name="point">The base Dynamo geometric point.</param>
        /// <returns name="NtsPoint">The equivalent topological point in NTS.</returns>
        public static NtsPoint ByPoint(DynGeometry.Point point)
        {
            if (point == null) throw new ArgumentNullException(nameof(point));

            var coordinate = new NtsGeometry.CoordinateZ(point.X, point.Y, point.Z);

            var ntsPoint = NtsGeometry.GeometryFactory.Default.CreatePoint(coordinate);

            return new NtsPoint(ntsPoint);
        }

        /// <summary>
        /// Creates an NtsPoint directly from numeric coordinates.
        /// </summary>
        /// <param name="x">Easting coordinate (X)</param>
        /// <param name="y">Northing coordinate (Y)</param>
        /// <param name="z">Elevation (Z) - Optional</param>
        /// <returns name="NtsPoint">The topological point in NTS.</returns>
        public static NtsPoint ByCoordinates(double x, double y, double z = 0.0)
        {
            var coordinate = new NtsGeometry.CoordinateZ(x, y, z);
            var ntsPoint = NtsGeometry.GeometryFactory.Default.CreatePoint(coordinate);

            return new NtsPoint(ntsPoint);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Converts the NtsPoint back into the Dynamo visual environment.
        /// </summary>
        /// <returns name="Point">A native Dynamo Point.</returns>
        public DynGeometry.Point ToPoint()
        {
            double z = double.IsNaN(InternalPoint.Z) ? 0.0 : InternalPoint.Z;

            return DynGeometry.Point.ByCoordinates(InternalPoint.X, InternalPoint.Y, z);
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Gets the X coordinate.
        /// </summary>
        public double X => InternalPoint.X;

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        public double Y => InternalPoint.Y;

        /// <summary>
        /// Gets the Z coordinate. Returns 0 if the geometry is strictly 2D.
        /// </summary>
        public double Z => double.IsNaN(InternalPoint.Z) ? 0.0 : InternalPoint.Z;

        #endregion
    }
}
