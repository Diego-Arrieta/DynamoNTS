using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometries = NetTopologySuite.Geometries;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Represents a collection of topological points from NetTopologySuite.
    /// </summary>
    public class NtsMultiPoint : NtsGeometry
    {
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.MultiPoint InternalMultiPoint
        {
            get => (NtsGeometries.MultiPoint)InternalGeometry;
            set => InternalGeometry = value;
        }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsMultiPoint(NtsGeometries.MultiPoint multiPoint) : base(multiPoint)
        {
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsMultiPoint from a collection of individual NtsPoints.
        /// </summary>
        /// <param name="points">Collection of topological NtsPoint entities.</param>
        /// <returns name="multiPoint">The constructed NtsMultiPoint collection.</returns>
        public static NtsMultiPoint ByPoints(IEnumerable<NtsPoint> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var ntsPts = points
                .Where(p => p != null && !p.InternalGeometry.IsEmpty)
                .Select(p => p.InternalPoint)
                .ToArray();

            var factory = NtsGeometries.GeometryFactory.Default;
            return new NtsMultiPoint(factory.CreateMultiPoint(ntsPts));
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Converts the MultiPoint parts into native Dynamo Points.
        /// </summary>
        /// <returns name="points">List of native Dynamo Point geometries.</returns>
        public List<DynGeometry.Point> ToPoints()
        {
            var list = new List<DynGeometry.Point>();
            for (int i = 0; i < InternalMultiPoint.NumGeometries; i++)
            {
                if (InternalMultiPoint.GetGeometryN(i) is NtsGeometries.Point pt)
                {
                    var wrapper = new NtsPoint(pt);
                    list.Add(wrapper.ToPoint());
                }
            }
            return list;
        }

        /// <summary>
        /// Explodes the MultiPoint into individual NtsPoint instances.
        /// </summary>
        /// <returns name="points">List of individual NtsPoint components.</returns>
        public List<NtsPoint> Explode()
        {
            var list = new List<NtsPoint>();
            for (int i = 0; i < InternalMultiPoint.NumGeometries; i++)
            {
                if (InternalMultiPoint.GetGeometryN(i) is NtsGeometries.Point pt)
                {
                    list.Add(new NtsPoint(pt));
                }
            }
            return list;
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Gets the total number of points in the MultiPoint collection.
        /// </summary>
        public int Count => InternalMultiPoint.NumGeometries;

        #endregion
    }
}