using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometries = NetTopologySuite.Geometries;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Abstract base class for all topological geometries from NetTopologySuite in Dynamo.
    /// </summary>
    public abstract class NtsGeometry
    {
        /// <summary>
        /// Internal generic NTS geometry instance.
        /// Hidden from the Dynamo user interface.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.Geometry InternalGeometry { get; set; }

        /// <summary>
        /// Protected base constructor to initialize the geometry.
        /// </summary>
        /// <param name="geometry">The base NetTopologySuite geometry.</param>
        [IsVisibleInDynamoLibrary(false)]
        protected NtsGeometry(NtsGeometries.Geometry geometry)
        {
            InternalGeometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        }

        #region Spatial Metadata Properties

        /// <summary>
        /// Gets the formal OGC geometry type of this instance.
        /// </summary>
        public virtual NtsGeometryType GeometryType => this switch
        {
            NtsPoint => NtsGeometryType.Point,
            NtsLineString => NtsGeometryType.LineString,
            NtsPolygon => NtsGeometryType.Polygon,
            NtsMultiPoint => NtsGeometryType.MultiPoint,
            NtsMultiLineString => NtsGeometryType.MultiLineString,
            NtsMultiPolygon => NtsGeometryType.MultiPolygon,
            _ => NtsGeometryType.None
        };

        /// <summary>
        /// Indicates whether this geometry contains valid three-dimensional elevation coordinates (Z).
        /// </summary>
        public bool HasZ
        {
            get
            {
                if (InternalGeometry == null || InternalGeometry.IsEmpty) return false;
                var coords = InternalGeometry.Coordinates;
                if (coords == null || coords.Length == 0) return false;
                return coords.Any(c => !double.IsNaN(c.Z));
            }
        }

        /// <summary>
        /// Alias indicating whether the geometry is 3D.
        /// </summary>
        public bool Is3D => HasZ;

        /// <summary>
        /// Gets the coordinate dimension: 3 if it contains Z coordinates, otherwise 2.
        /// </summary>
        public int CoordinateDimension => HasZ ? 3 : 2;

        #endregion

        #region Create Actions

        /// <summary>
        /// Computes the constructive topological intersection between this geometry and another geometry.
        /// </summary>
        /// <param name="other">Another topological geometry (NtsGeometry).</param>
        /// <returns name="NtsGeometry">The resulting geometry representing the shared points, or null if they do not intersect.</returns>
        public NtsGeometry? Intersection(NtsGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            var result = this.InternalGeometry.Intersection(other.InternalGeometry);
            return WrapGeometry(result);
        }

        /// <summary>
        /// Computes the topological difference between this geometry and another geometry (A - B).
        /// </summary>
        /// <param name="other">The geometry to subtract.</param>
        /// <returns name="NtsGeometry">The resulting geometry representing the difference.</returns>
        public NtsGeometry? Difference(NtsGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            var result = this.InternalGeometry.Difference(other.InternalGeometry);
            return WrapGeometry(result);
        }

        /// <summary>
        /// Computes the topological union of this geometry and another geometry (A + B).
        /// </summary>
        /// <param name="other">The geometry to combine with this one.</param>
        /// <returns name="NtsGeometry">The resulting merged geometry.</returns>
        public NtsGeometry? Union(NtsGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            var result = this.InternalGeometry.Union(other.InternalGeometry);
            return WrapGeometry(result);
        }

        /// <summary>
        /// Computes a buffer area around the geometry at a specified distance.
        /// </summary>
        /// <param name="distance">The buffer distance (positive or negative for polygons).</param>
        /// <returns name="NtsGeometry">The resulting buffered geometry.</returns>
        public NtsGeometry? Buffer(double distance)
        {
            var resultGeometry = this.InternalGeometry.Buffer(distance);
            return WrapGeometry(resultGeometry);
        }

        /// <summary>
        /// Produces a copy of the geometry forced to 2D by clearing all elevation (Z) components.
        /// </summary>
        /// <returns name="geometry">A 2D clone of the geometry.</returns>
        public NtsGeometry? Force2D()
        {
            if (this.InternalGeometry == null || this.InternalGeometry.IsEmpty) return null;

            var cloned = (NtsGeometries.Geometry)this.InternalGeometry.Copy();
            var filter = new DropZFilter();
            cloned.Apply(filter);
            cloned.GeometryChanged();

            return WrapGeometry(cloned);
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Calculates the minimum Euclidean distance between this geometry and another geometry.
        /// </summary>
        /// <param name="other">Another topological geometry (NtsGeometry).</param>
        /// <returns name="double">The shortest distance between both geometries in model units.</returns>
        public double DistanceTo(NtsGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            return this.InternalGeometry.Distance(other.InternalGeometry);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Converts a raw NetTopologySuite geometry into its corresponding Dynamo wrapper class.
        /// Automatically unpacks and homogenizes GeometryCollections to their dominant dimension.
        /// </summary>
        /// <param name="ntsGeometry">The raw geometry generated by NTS.</param>
        /// <returns>An instance of NtsPoint, NtsLineString, NtsPolygon, Multi-counterpart, or null if empty.</returns>
        [IsVisibleInDynamoLibrary(false)]
        internal static NtsGeometry? WrapGeometry(NtsGeometries.Geometry ntsGeometry)
        {
            if (ntsGeometry == null || ntsGeometry.IsEmpty) return null;

            if (ntsGeometry is NtsGeometries.Point p) return new NtsPoint(p);
            if (ntsGeometry is NtsGeometries.LineString ls) return new NtsLineString(ls);
            if (ntsGeometry is NtsGeometries.Polygon poly) return new NtsPolygon(poly);

            if (ntsGeometry is NtsGeometries.MultiPoint mp) return new NtsMultiPoint(mp);
            if (ntsGeometry is NtsGeometries.MultiLineString mls) return new NtsMultiLineString(mls);
            if (ntsGeometry is NtsGeometries.MultiPolygon mpoly) return new NtsMultiPolygon(mpoly);

            if (ntsGeometry is NtsGeometries.GeometryCollection gc)
            {
                var polys = new List<NtsGeometries.Polygon>();
                var lines = new List<NtsGeometries.LineString>();
                var points = new List<NtsGeometries.Point>();

                FlattenGeometryCollection(gc, polys, lines, points);

                var factory = ntsGeometry.Factory ?? NtsGeometries.GeometryFactory.Default;

                if (polys.Count > 0)
                {
                    if (polys.Count == 1) return new NtsPolygon(polys[0]);
                    return new NtsMultiPolygon(factory.CreateMultiPolygon(polys.ToArray()));
                }

                if (lines.Count > 0)
                {
                    if (lines.Count == 1) return new NtsLineString(lines[0]);
                    return new NtsMultiLineString(factory.CreateMultiLineString(lines.ToArray()));
                }

                if (points.Count > 0)
                {
                    if (points.Count == 1) return new NtsPoint(points[0]);
                    return new NtsMultiPoint(factory.CreateMultiPoint(points.ToArray()));
                }

                return null;
            }

            throw new NotSupportedException($"The resulting geometry type '{ntsGeometry.GeometryType}' is not currently supported in Dynamo wrapper classes.");
        }

        private static void FlattenGeometryCollection(
            NtsGeometries.Geometry geom,
            List<NtsGeometries.Polygon> polys,
            List<NtsGeometries.LineString> lines,
            List<NtsGeometries.Point> points)
        {
            if (geom == null || geom.IsEmpty) return;

            if (geom is NtsGeometries.Polygon p)
            {
                polys.Add(p);
            }
            else if (geom is NtsGeometries.LineString ls)
            {
                lines.Add(ls);
            }
            else if (geom is NtsGeometries.Point pt)
            {
                points.Add(pt);
            }
            else if (geom is NtsGeometries.MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    if (mp.GetGeometryN(i) is NtsGeometries.Polygon poly) polys.Add(poly);
                }
            }
            else if (geom is NtsGeometries.MultiLineString mls)
            {
                for (int i = 0; i < mls.NumGeometries; i++)
                {
                    if (mls.GetGeometryN(i) is NtsGeometries.LineString l) lines.Add(l);
                }
            }
            else if (geom is NtsGeometries.MultiPoint mpts)
            {
                for (int i = 0; i < mpts.NumGeometries; i++)
                {
                    if (mpts.GetGeometryN(i) is NtsGeometries.Point p0) points.Add(p0);
                }
            }
            else if (geom is NtsGeometries.GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                {
                    FlattenGeometryCollection(gc.GetGeometryN(i), polys, lines, points);
                }
            }
        }

        #endregion

        #region Internal Sequence Filters

        private class DropZFilter : NtsGeometries.ICoordinateSequenceFilter
        {
            public bool Done => false;
            public bool GeometryChanged => true;

            public void Filter(NtsGeometries.CoordinateSequence seq, int i)
            {
                seq.SetZ(i, double.NaN);
            }
        }

        #endregion
    }
}