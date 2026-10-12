using Autodesk.DesignScript.Runtime;
using DynamoNTS.Features;
using DynamoNTS.Geometry;
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
        /// <param name="source">The source coordinate system.</param>
        /// <param name="target">The target coordinate system.</param>
        /// <returns name="NtsGeometry">A new NtsGeometry reprojected into the target CRS.</returns>
        public static NtsGeometry TransformGeometry(
            NtsGeometry geometry,
            CoordinateSystem source,
            CoordinateSystem target)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            // Si ambos sistemas tienen el mismo EPSG no nulo, retorna una copia de la geometría
            if (source.EPSG != 0 && source.EPSG == target.EPSG)
            {
                var cloneSame = (NetTopologySuite.Geometries.Geometry)geometry.InternalGeometry.Copy();
                return NtsGeometry.WrapGeometry(cloneSame);
            }

            var transformFactory = new CoordinateTransformationFactory();
            var transformation = transformFactory.CreateFromCoordinateSystems(
                source.InternalCRS,
                target.InternalCRS);

            // Clonar la geometría interna para evitar mutar el objeto original en memoria
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
        /// <param name="source">The source coordinate system.</param>
        /// <param name="target">The target coordinate system.</param>
        /// <returns name="NtsFeature">A new NtsFeature with the reprojected geometry and original attributes.</returns>
        public static NtsFeature TransformFeature(
            NtsFeature feature,
            CoordinateSystem source,
            CoordinateSystem target)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var reprojectedGeometry = TransformGeometry(feature.Geometry, source, target);
            return NtsFeature.Create(reprojectedGeometry, feature.Attributes);
        }

        /// <summary>
        /// Reprojects a single 2D coordinate pair directly.
        /// </summary>
        /// <param name="x">X / Easting / Longitude coordinate.</param>
        /// <param name="y">Y / Northing / Latitude coordinate.</param>
        /// <param name="source">Source coordinate system.</param>
        /// <param name="target">Target coordinate system.</param>
        /// <returns name="double[]">An array containing the transformed [X, Y] coordinates.</returns>
        public static double[] TransformPoint(
            double x,
            double y,
            CoordinateSystem source,
            CoordinateSystem target)
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