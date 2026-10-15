using Autodesk.DesignScript.Runtime;
using DynamoNTS.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using System;

namespace DynamoNTS.Analysis
{
    /// <summary>
    /// Topological relational boolean operators (True/False) powered by NetTopologySuite.
    /// Includes resilient fallback execution against non-valid geometries using GeometryFixer.
    /// </summary>
    public static class Relations
    {
        #region Action Nodes

        /// <summary>
        /// Determines whether two geometries topologically intersect (share at least one point in common).
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns>True if the geometries intersect; otherwise, false.</returns>
        public static bool Intersects(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return false;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.Intersects(g2));
        }

        /// <summary>
        /// Determines whether geometry 'a' topologically contains geometry 'b'.
        /// Note: Points on the boundary of 'a' are not considered contained in its interior.
        /// </summary>
        /// <param name="a">The container (host) geometry.</param>
        /// <param name="b">The target geometry.</param>
        /// <returns>True if geometry 'b' lies entirely within geometry 'a'; otherwise, false.</returns>
        public static bool Contains(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return false;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.Contains(g2));
        }

        /// <summary>
        /// Determines whether geometry 'a' topologically lies within geometry 'b' (inverse of Contains).
        /// </summary>
        /// <param name="a">The target geometry.</param>
        /// <param name="b">The container geometry.</param>
        /// <returns>True if geometry 'a' lies within geometry 'b'; otherwise, false.</returns>
        public static bool Within(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return false;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.Within(g2));
        }

        /// <summary>
        /// Determines whether geometry 'a' covers geometry 'b'.
        /// Unlike Contains, this returns true even if parts of 'b' lie on the boundary of 'a'.
        /// </summary>
        /// <param name="a">The covering geometry.</param>
        /// <param name="b">The target geometry.</param>
        /// <returns>True if every point of 'b' is a point of 'a'; otherwise, false.</returns>
        public static bool Covers(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return false;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.Covers(g2));
        }

        /// <summary>
        /// Determines whether geometry 'a' is covered by geometry 'b' (inverse of Covers).
        /// </summary>
        /// <param name="a">The target geometry.</param>
        /// <param name="b">The covering geometry.</param>
        /// <returns>True if every point of 'a' is a point of 'b'; otherwise, false.</returns>
        public static bool CoveredBy(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return false;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.CoveredBy(g2));
        }

        /// <summary>
        /// Determines whether two geometries touch at their boundaries without their interiors overlapping.
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns>True if the geometries touch at a boundary point; otherwise, false.</returns>
        public static bool Touches(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return false;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.Touches(g2));
        }

        /// <summary>
        /// Determines whether two geometries cross each other (their interiors intersect in points of lower dimension).
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns>True if the geometries cross; otherwise, false.</returns>
        public static bool Crosses(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return false;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.Crosses(g2));
        }

        /// <summary>
        /// Determines whether two geometries of the same dimension partially overlap.
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns>True if the geometries overlap; otherwise, false.</returns>
        public static bool Overlaps(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return false;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.Overlaps(g2));
        }

        /// <summary>
        /// Determines whether two geometries are completely disjoint (share no points in common).
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns>True if the geometries are disjoint; otherwise, false.</returns>
        public static bool Disjoint(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            if (a.InternalGeometry.IsEmpty || b.InternalGeometry.IsEmpty)
                return true;

            return SafePredicate(a.InternalGeometry, b.InternalGeometry, (g1, g2) => g1.Disjoint(g2));
        }

        #endregion

        #region Internal Resilient Predicate Helper

        private static bool SafePredicate(
            NetTopologySuite.Geometries.Geometry geomA,
            NetTopologySuite.Geometries.Geometry geomB,
            Func<NetTopologySuite.Geometries.Geometry, NetTopologySuite.Geometries.Geometry, bool> predicate)
        {
            try
            {
                return predicate(geomA, geomB);
            }
            catch (TopologyException)
            {
                var fixedA = geomA.IsValid ? geomA : GeometryFixer.Fix(geomA);
                var fixedB = geomB.IsValid ? geomB : GeometryFixer.Fix(geomB);

                try
                {
                    return predicate(fixedA, fixedB);
                }
                catch (TopologyException)
                {
                    fixedA = GeometryFixer.Fix(geomA);
                    fixedB = GeometryFixer.Fix(geomB);
                    return predicate(fixedA, fixedB);
                }
            }
        }

        #endregion
    }
}