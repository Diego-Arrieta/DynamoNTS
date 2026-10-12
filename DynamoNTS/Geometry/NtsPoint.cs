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
    /// Represents a topological point from NetTopologySuite.
    /// </summary>
    public class NtsPoint : NtsGeometry
    {
        /// <summary>
        /// Internal NTS geometry.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.Point InternalPoint
        {
            get => (NtsGeometries.Point)InternalGeometry;
            set => InternalGeometry = value;
        }

        /// <summary>
        /// Internal constructor.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsPoint(NtsGeometries.Point point) : base(point)
        {
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

            var coordinate = new NtsGeometries.CoordinateZ(point.X, point.Y, point.Z);
            var ntsPoint = NtsGeometries.GeometryFactory.Default.CreatePoint(coordinate);

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
            var coordinate = new NtsGeometries.CoordinateZ(x, y, z);
            var ntsPoint = NtsGeometries.GeometryFactory.Default.CreatePoint(coordinate);

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
