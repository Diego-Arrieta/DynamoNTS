using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometries = NetTopologySuite.Geometries;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Represents a collection of topological polygons from NetTopologySuite.
    /// </summary>
    public class NtsMultiPolygon : NtsGeometry
    {
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.MultiPolygon InternalMultiPolygon
        {
            get => (NtsGeometries.MultiPolygon)InternalGeometry;
            set => InternalGeometry = value;
        }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsMultiPolygon(NtsGeometries.MultiPolygon multiPolygon) : base(multiPolygon)
        {
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsMultiPolygon from a collection of individual NtsPolygon instances.
        /// </summary>
        public static NtsMultiPolygon ByPolygons(IEnumerable<NtsPolygon> polygons)
        {
            if (polygons == null) throw new ArgumentNullException(nameof(polygons));

            var ntsPolys = polygons
                .Where(p => p != null && !p.InternalGeometry.IsEmpty)
                .Select(p => p.InternalPolygon)
                .ToArray();

            if (ntsPolys.Length == 0)
                throw new ArgumentException("At least one valid polygon is required to create a MultiPolygon.");

            var factory = NtsGeometries.GeometryFactory.Default;
            var multiPoly = factory.CreateMultiPolygon(ntsPolys);

            return new NtsMultiPolygon(multiPoly);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Converts the MultiPolygon parts into native Dynamo Polygons.
        /// </summary>
        public List<DynGeometry.Polygon> ToPolygons()
        {
            var results = new List<DynGeometry.Polygon>();

            for (int i = 0; i < InternalMultiPolygon.NumGeometries; i++)
            {
                if (InternalMultiPolygon.GetGeometryN(i) is NtsGeometries.Polygon poly)
                {
                    var wrapper = new NtsPolygon(poly);
                    results.Add(wrapper.ToPolygon());
                }
            }

            return results;
        }

        /// <summary>
        /// Explodes the MultiPolygon into individual NtsPolygon instances.
        /// </summary>
        public List<NtsPolygon> Explode()
        {
            var results = new List<NtsPolygon>();
            for (int i = 0; i < InternalMultiPolygon.NumGeometries; i++)
            {
                if (InternalMultiPolygon.GetGeometryN(i) is NtsGeometries.Polygon poly)
                {
                    results.Add(new NtsPolygon(poly));
                }
            }
            return results;
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Gets the total number of polygon parts.
        /// </summary>
        public int Count => InternalMultiPolygon.NumGeometries;

        /// <summary>
        /// Total combined area of all polygon parts.
        /// </summary>
        public double Area => InternalMultiPolygon.Area;

        /// <summary>
        /// Total perimeter of all polygon parts.
        /// </summary>
        public double Perimeter => InternalMultiPolygon.Length;

        /// <summary>
        /// Center of mass (centroid) of the entire MultiPolygon.
        /// </summary>
        public NtsPoint Centroid => new NtsPoint(InternalMultiPolygon.Centroid);

        #endregion
    }
}
