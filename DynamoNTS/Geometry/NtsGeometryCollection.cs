using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using NtsGeometries = NetTopologySuite.Geometries;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Represents a heterogeneous or composite collection of topological geometries from NetTopologySuite.
    /// </summary>
    public class NtsGeometryCollection : NtsGeometry
    {
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.GeometryCollection InternalCollection
        {
            get => (NtsGeometries.GeometryCollection)InternalGeometry;
            set => InternalGeometry = value;
        }

        /// <summary>
        /// Gets the formal geometry type, always returning Mixed for heterogeneous collections.
        /// </summary>
        public override NtsGeometryType GeometryType => NtsGeometryType.Mixed;

        /// <summary>
        /// Gets the distinct geometries contained within this collection.
        /// </summary>
        public List<NtsGeometry> Geometries { get; }

        /// <summary>
        /// Gets the total number of geometries contained in this collection.
        /// </summary>
        public int Count => Geometries.Count;

        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometryCollection(NtsGeometries.GeometryCollection collection, List<NtsGeometry> children)
            : base(collection)
        {
            Geometries = children ?? new List<NtsGeometry>();
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsGeometryCollection from an arbitrary list of geometries.
        /// </summary>
        /// <param name="geometries">The list of geometries to combine.</param>
        /// <returns>The composite geometry collection.</returns>
        public static NtsGeometryCollection ByGeometries(IEnumerable<NtsGeometry> geometries)
        {
            if (geometries == null) throw new ArgumentNullException(nameof(geometries));

            var validGeoms = new List<NtsGeometry>();
            var ntsList = new List<NtsGeometries.Geometry>();

            foreach (var g in geometries)
            {
                if (g?.InternalGeometry != null && !g.InternalGeometry.IsEmpty)
                {
                    validGeoms.Add(g);
                    ntsList.Add(g.InternalGeometry);
                }
            }

            var factory = NtsGeometries.GeometryFactory.Default;
            var collection = factory.CreateGeometryCollection(ntsList.ToArray());

            return new NtsGeometryCollection(collection, validGeoms);
        }

        #endregion

        #region Action Nodes

        /// <summary>
        /// Deconstructs the collection into individual atomic geometries.
        /// </summary>
        /// <returns>List of individual NtsGeometry components.</returns>
        public List<NtsGeometry> Explode()
        {
            return new List<NtsGeometry>(Geometries);
        }

        #endregion
    }
}
