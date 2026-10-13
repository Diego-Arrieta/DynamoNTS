using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
using DynamoNTS.Geometry;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems.Transformations;
using System;

namespace DynamoNTS.Projections
{
    /// <summary>
    /// Provides mathematical coordinate reprojection and transformations between Coordinate Reference Systems.
    /// </summary>
    public static class Reprojector
    {
        #region Action Nodes

        /// <summary>
        /// Reprojects an NtsGeometry from a source Coordinate Reference System to a target Coordinate Reference System.
        /// </summary>
        /// <param name="geometry">The topological geometry to reproject.</param>
        /// <param name="source">The source coordinate reference system.</param>
        /// <param name="target">The target coordinate reference system.</param>
        /// <returns name="geometry">A new NtsGeometry reprojected into the target CRS, or null if geometry is empty.</returns>
        public static NtsGeometry? TransformGeometry(
            NtsGeometry geometry,
            NtsCoordinateSystem source,
            NtsCoordinateSystem target)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            // If both systems share the same non-zero EPSG code, return a clone of the geometry
            if (source.EPSG != 0 && source.EPSG == target.EPSG)
            {
                var cloneSame = (NetTopologySuite.Geometries.Geometry)geometry.InternalGeometry.Copy();
                return NtsGeometry.WrapGeometry(cloneSame);
            }

            var transformFactory = new CoordinateTransformationFactory();
            var transformation = transformFactory.CreateFromCoordinateSystems(
                source.InternalCRS,
                target.InternalCRS);

            // Clone internal geometry to avoid mutating the original instance in memory
            var cloned = (NetTopologySuite.Geometries.Geometry)geometry.InternalGeometry.Copy();
            var filter = new CoordinateTransformationFilter(transformation.MathTransform);
            cloned.Apply(filter);
            cloned.GeometryChanged();

            if (target.EPSG != 0)
            {
                cloned.SRID = target.EPSG;
            }

            return NtsGeometry.WrapGeometry(cloned);
        }

        /// <summary>
        /// Reprojects an entire NtsFeature, transforming its geometry while preserving all tabular attributes intact.
        /// </summary>
        /// <param name="feature">The GIS feature to reproject.</param>
        /// <param name="source">The source coordinate reference system.</param>
        /// <param name="target">The target coordinate reference system.</param>
        /// <returns name="feature">A new NtsFeature with reprojected geometry and preserved attributes.</returns>
        public static NtsFeature TransformFeature(
            NtsFeature feature,
            NtsCoordinateSystem source,
            NtsCoordinateSystem target)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var reprojectedGeometry = TransformGeometry(feature.Geometry, source, target);
            if (reprojectedGeometry == null)
            {
                throw new InvalidOperationException("Failed to reproject feature geometry.");
            }

            var concreteFeature = new Feature(reprojectedGeometry.InternalGeometry, feature.Record.InternalTable);
            return new NtsFeature(concreteFeature, reprojectedGeometry, feature.Record);
        }

        /// <summary>
        /// Reprojects a single 2D coordinate pair directly.
        /// </summary>
        /// <param name="x">X / Easting / Longitude coordinate.</param>
        /// <param name="y">Y / Northing / Latitude coordinate.</param>
        /// <param name="source">Source coordinate reference system.</param>
        /// <param name="target">Target coordinate reference system.</param>
        /// <returns name="coordinates">An array containing the transformed [X, Y] coordinates.</returns>
        public static double[] TransformPoint(
            double x,
            double y,
            NtsCoordinateSystem source,
            NtsCoordinateSystem target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var transformFactory = new CoordinateTransformationFactory();
            var transformation = transformFactory.CreateFromCoordinateSystems(
                source.InternalCRS,
                target.InternalCRS);

            return transformation.MathTransform.Transform(new[] { x, y });
        }

        #endregion

        #region Internal Sequence Filter

        private class CoordinateTransformationFilter : ICoordinateSequenceFilter
        {
            private readonly MathTransform _mathTransform;

            public CoordinateTransformationFilter(MathTransform mathTransform)
            {
                _mathTransform = mathTransform ?? throw new ArgumentNullException(nameof(mathTransform));
            }

            public bool Done => false;
            public bool GeometryChanged => true;

            public void Filter(CoordinateSequence seq, int i)
            {
                double x = seq.GetX(i);
                double y = seq.GetY(i);

                double[] transformed = _mathTransform.Transform(new[] { x, y });

                seq.SetX(i, transformed[0]);
                seq.SetY(i, transformed[1]);
            }
        }

        #endregion
    }
}