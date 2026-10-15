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
    /// Supports both 2D planar transformations and 3D coordinate transformations with elevation preservation.
    /// </summary>
    public static class Reprojector
    {
        #region Action Nodes

        /// <summary>
        /// Reprojects an NtsGeometry from a source Coordinate Reference System to a target Coordinate Reference System.
        /// Preserves elevation (Z) coordinates, transforming them when a 3D datum shift is required.
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

            if (source.IsEquivalentTo(target))
            {
                var copy = (NetTopologySuite.Geometries.Geometry)geometry.InternalGeometry.Copy();
                if (target.EPSG != 0)
                {
                    copy.SRID = target.EPSG;
                }
                return NtsGeometry.WrapGeometry(copy);
            }

            var filter = CreateFilter(source, target);
            return TransformGeometry(geometry, filter, target.EPSG);
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

            if (source.IsEquivalentTo(target))
            {
                return feature.Clone();
            }

            var filter = CreateFilter(source, target);
            return TransformFeature(feature, filter, target.EPSG);
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

            if (source.IsEquivalentTo(target))
            {
                return new[] { x, y };
            }

            var transformFactory = new CoordinateTransformationFactory();
            var transformation = transformFactory.CreateFromCoordinateSystems(
                source.InternalCRS,
                target.InternalCRS);

            var mathTransform = transformation.MathTransform;
            if (mathTransform.DimSource >= 3)
            {
                var input3D = new[] { x, y, 0.0 };
                var output3D = mathTransform.Transform(input3D);
                return new[] { output3D[0], output3D[1] };
            }

            return mathTransform.Transform(new[] { x, y });
        }

        /// <summary>
        /// Reprojects a single 3D coordinate triplet directly, preserving or transforming elevation.
        /// </summary>
        /// <param name="x">X / Easting / Longitude coordinate.</param>
        /// <param name="y">Y / Northing / Latitude coordinate.</param>
        /// <param name="z">Z / Elevation coordinate.</param>
        /// <param name="source">Source coordinate reference system.</param>
        /// <param name="target">Target coordinate reference system.</param>
        /// <returns name="coordinates">An array containing the transformed [X, Y, Z] coordinates.</returns>
        public static double[] TransformPoint3D(
            double x,
            double y,
            double z,
            NtsCoordinateSystem source,
            NtsCoordinateSystem target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (source.IsEquivalentTo(target))
            {
                return new[] { x, y, z };
            }

            var transformFactory = new CoordinateTransformationFactory();
            var transformation = transformFactory.CreateFromCoordinateSystems(
                source.InternalCRS,
                target.InternalCRS);

            var mathTransform = transformation.MathTransform;
            if (mathTransform.DimSource >= 3)
            {
                return mathTransform.Transform(new[] { x, y, z });
            }

            var xy = mathTransform.Transform(new[] { x, y });
            return new[] { xy[0], xy[1], z };
        }

        #endregion

        #region Internal Batch Helpers

        [IsVisibleInDynamoLibrary(false)]
        internal static CoordinateTransformationFilter CreateFilter(
            NtsCoordinateSystem source,
            NtsCoordinateSystem target)
        {
            var transformFactory = new CoordinateTransformationFactory();
            var transformation = transformFactory.CreateFromCoordinateSystems(
                source.InternalCRS,
                target.InternalCRS);

            return new CoordinateTransformationFilter(transformation.MathTransform);
        }

        [IsVisibleInDynamoLibrary(false)]
        internal static NtsGeometry? TransformGeometry(
            NtsGeometry geometry,
            CoordinateTransformationFilter filter,
            int targetSrid = 0)
        {
            if (geometry == null) return null;

            var cloned = (NetTopologySuite.Geometries.Geometry)geometry.InternalGeometry.Copy();
            cloned.Apply(filter);
            cloned.GeometryChanged();

            if (targetSrid != 0)
            {
                cloned.SRID = targetSrid;
            }

            return NtsGeometry.WrapGeometry(cloned);
        }

        [IsVisibleInDynamoLibrary(false)]
        internal static NtsFeature TransformFeature(
            NtsFeature feature,
            CoordinateTransformationFilter filter,
            int targetSrid = 0)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));

            var reprojectedGeometry = TransformGeometry(feature.Geometry, filter, targetSrid);
            if (reprojectedGeometry == null)
            {
                throw new InvalidOperationException("Failed to reproject feature geometry.");
            }

            var clonedRecord = feature.Record.Clone();
            var concreteFeature = new Feature(reprojectedGeometry.InternalGeometry, clonedRecord.InternalTable);
            return new NtsFeature(concreteFeature, reprojectedGeometry, clonedRecord);
        }

        #endregion

        #region Internal Sequence Filter

        [IsVisibleInDynamoLibrary(false)]
        internal class CoordinateTransformationFilter : ICoordinateSequenceFilter
        {
            private readonly MathTransform _mathTransform;
            private readonly double[] _coordBuffer;

            public CoordinateTransformationFilter(MathTransform mathTransform)
            {
                _mathTransform = mathTransform ?? throw new ArgumentNullException(nameof(mathTransform));
                _coordBuffer = new double[Math.Max(2, _mathTransform.DimSource)];
            }

            public bool Done => false;
            public bool GeometryChanged => true;

            public void Filter(CoordinateSequence seq, int i)
            {
                _coordBuffer[0] = seq.GetX(i);
                _coordBuffer[1] = seq.GetY(i);

                bool hasZ = seq.HasZ || seq.Dimension >= 3;

                if (_coordBuffer.Length >= 3)
                {
                    double z = hasZ ? seq.GetZ(i) : double.NaN;
                    _coordBuffer[2] = double.IsNaN(z) ? 0.0 : z;
                }

                double[] transformed = _mathTransform.Transform(_coordBuffer);

                seq.SetX(i, transformed[0]);
                seq.SetY(i, transformed[1]);

                if (hasZ && transformed.Length >= 3)
                {
                    seq.SetZ(i, transformed[2]);
                }
            }
        }

        #endregion
    }
}