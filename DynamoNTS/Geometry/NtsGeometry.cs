using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using NtsGeometries = NetTopologySuite.Geometries;
using NtsUtilities = NetTopologySuite.Geometries.Utilities;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Abstract base class representing topological geometries backed by NetTopologySuite within Dynamo.
    /// </summary>
    public abstract class NtsGeometry
    {
        private NtsGeometries.Geometry _internalGeometry;

        [IsVisibleInDynamoLibrary(false)]
        private bool? _hasZ;

        /// <summary>
        /// Gets or sets the underlying NetTopologySuite geometry instance.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.Geometry InternalGeometry
        {
            get => _internalGeometry;
            set
            {
                _internalGeometry = value ?? throw new ArgumentNullException(nameof(value));
                _hasZ = null;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NtsGeometry"/> class.
        /// </summary>
        /// <param name="geometry">The underlying NetTopologySuite geometry.</param>
        [IsVisibleInDynamoLibrary(false)]
        protected NtsGeometry(NtsGeometries.Geometry geometry)
        {
            _internalGeometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
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
                if (_hasZ.HasValue) return _hasZ.Value;

                if (_internalGeometry == null || _internalGeometry.IsEmpty)
                {
                    _hasZ = false;
                    return false;
                }

                var filter = new HasZSequenceFilter();
                _internalGeometry.Apply(filter);

                _hasZ = filter.FoundZ;
                return _hasZ.Value;
            }
        }

        /// <summary>
        /// Indicates whether this geometry is three-dimensional.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        public bool Is3D => HasZ;

        /// <summary>
        /// Gets the coordinate dimension: 3 if elevation coordinates (Z) exist, otherwise 2.
        /// </summary>
        public int CoordinateDimension => HasZ ? 3 : 2;

        #endregion

        #region Create Actions

        /// <summary>
        /// Computes the topological intersection between this geometry and another geometry.
        /// Retries with GeometryFixer if a TopologyException occurs.
        /// </summary>
        /// <param name="other">The target geometry to intersect with.</param>
        /// <returns>The resulting intersection geometry, or null if no intersection exists.</returns>
        public NtsGeometry? Intersection(NtsGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            var result = ExecuteOverlay(this.InternalGeometry, other.InternalGeometry, (a, b) => a.Intersection(b));
            return WrapGeometry(result);
        }

        /// <summary>
        /// Computes the topological difference between this geometry and another geometry (this - other).
        /// Retries with GeometryFixer if a TopologyException occurs.
        /// </summary>
        /// <param name="other">The geometry to subtract.</param>
        /// <returns>The resulting difference geometry, or null if the result is empty.</returns>
        public NtsGeometry? Difference(NtsGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            var result = ExecuteOverlay(this.InternalGeometry, other.InternalGeometry, (a, b) => a.Difference(b));
            return WrapGeometry(result);
        }

        /// <summary>
        /// Computes the topological union of this geometry and another geometry.
        /// Retries with GeometryFixer if a TopologyException occurs.
        /// </summary>
        /// <param name="other">The geometry to combine with this instance.</param>
        /// <returns>The resulting merged geometry.</returns>
        public NtsGeometry? Union(NtsGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            var result = ExecuteOverlay(this.InternalGeometry, other.InternalGeometry, (a, b) => a.Union(b));
            return WrapGeometry(result);
        }

        /// <summary>
        /// Computes a buffer polygon around the geometry at the specified distance.
        /// Retries with GeometryFixer if a TopologyException occurs.
        /// </summary>
        /// <param name="distance">The buffer distance in coordinate reference system units.</param>
        /// <returns>The resulting buffered polygon geometry.</returns>
        public NtsGeometry? Buffer(double distance)
        {
            var resultGeometry = ExecuteBuffer(this.InternalGeometry, distance);
            return WrapGeometry(resultGeometry);
        }

        /// <summary>
        /// Creates a two-dimensional copy of this geometry by clearing all elevation (Z) components.
        /// </summary>
        /// <returns>A strictly 2D clone of this geometry, or null if the source geometry is empty.</returns>
        public NtsGeometry? Force2D()
        {
            if (this.InternalGeometry == null || this.InternalGeometry.IsEmpty) return null;

            if (!this.HasZ)
            {
                var clone2D = WrapGeometry((NtsGeometries.Geometry)this.InternalGeometry.Copy());
                if (clone2D != null) clone2D._hasZ = false;
                return clone2D;
            }

            var cloned = (NtsGeometries.Geometry)this.InternalGeometry.Copy();
            var filter = new DropZFilter();
            cloned.Apply(filter);
            cloned.GeometryChanged();

            var wrapped = WrapGeometry(cloned);
            if (wrapped != null)
            {
                wrapped._hasZ = false;
            }

            return wrapped;
        }

        #endregion

        #region Query Nodes

        /// <summary>
        /// Computes the minimum Euclidean distance between this geometry and another geometry.
        /// </summary>
        /// <param name="other">The target geometry.</param>
        /// <returns>The minimum Euclidean distance in coordinate units.</returns>
        public double DistanceTo(NtsGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            return this.InternalGeometry.Distance(other.InternalGeometry);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Wraps a raw NetTopologySuite geometry instance into its corresponding DynamoNTS wrapper class.
        /// Flattens heterogeneous or nested GeometryCollections to their dominant spatial dimension.
        /// </summary>
        /// <param name="ntsGeometry">The raw NetTopologySuite geometry.</param>
        /// <returns>The corresponding typed <see cref="NtsGeometry"/> instance, or null if empty.</returns>
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

                int dimensionsCount = (polys.Count > 0 ? 1 : 0)
                                    + (lines.Count > 0 ? 1 : 0)
                                    + (points.Count > 0 ? 1 : 0);

                // Caso Mixto: sin pérdida de geometrías usando NtsGeometryCollection
                if (dimensionsCount > 1)
                {
                    var children = new List<NtsGeometry>();
                    foreach (var pItem in polys) children.Add(new NtsPolygon(pItem));
                    foreach (var lItem in lines) children.Add(new NtsLineString(lItem));
                    foreach (var ptItem in points) children.Add(new NtsPoint(ptItem));

                    return new NtsGeometryCollection(gc, children);
                }

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

            throw new NotSupportedException($"The geometry type '{ntsGeometry.GeometryType}' is not currently supported in Dynamo wrapper classes.");
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

        private class HasZSequenceFilter : NtsGeometries.ICoordinateSequenceFilter
        {
            public bool FoundZ { get; private set; }
            public bool Done => FoundZ;
            public bool GeometryChanged => false;

            public void Filter(NtsGeometries.CoordinateSequence seq, int i)
            {
                if (seq.HasZ && !double.IsNaN(seq.GetZ(i)))
                {
                    FoundZ = true;
                }
            }
        }

        private class DropZFilter : NtsGeometries.ICoordinateSequenceFilter
        {
            public bool Done => false;
            public bool GeometryChanged { get; private set; }

            public void Filter(NtsGeometries.CoordinateSequence seq, int i)
            {
                if (!seq.HasZ) return;

                seq.SetZ(i, double.NaN);
                GeometryChanged = true;
            }
        }

        #endregion

        #region Boolean Robustness Helpers

        private static NtsGeometries.Geometry? ExecuteOverlay(
            NtsGeometries.Geometry geomA,
            NtsGeometries.Geometry geomB,
            Func<NtsGeometries.Geometry, NtsGeometries.Geometry, NtsGeometries.Geometry> operation)
        {
            try
            {
                return operation(geomA, geomB);
            }
            catch (NtsGeometries.TopologyException)
            {
                var fixedA = geomA.IsValid ? geomA : NtsUtilities.GeometryFixer.Fix(geomA);
                var fixedB = geomB.IsValid ? geomB : NtsUtilities.GeometryFixer.Fix(geomB);

                try
                {
                    return operation(fixedA, fixedB);
                }
                catch (NtsGeometries.TopologyException)
                {
                    fixedA = NtsUtilities.GeometryFixer.Fix(geomA);
                    fixedB = NtsUtilities.GeometryFixer.Fix(geomB);
                    return operation(fixedA, fixedB);
                }
            }
        }

        private static NtsGeometries.Geometry? ExecuteBuffer(NtsGeometries.Geometry geom, double distance)
        {
            try
            {
                return geom.Buffer(distance);
            }
            catch (NtsGeometries.TopologyException)
            {
                var fixedGeom = NtsUtilities.GeometryFixer.Fix(geom);
                return fixedGeom.Buffer(distance);
            }
        }

        #endregion
    }
}