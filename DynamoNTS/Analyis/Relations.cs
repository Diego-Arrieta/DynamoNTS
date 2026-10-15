using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using DynamoNTS.Geometry;

namespace DynamoNTS.Analysis
{
    /// <summary>
    /// Topological relational boolean operators (True/False) powered by NetTopologySuite.
    /// </summary>
    public static class Relations
    {
        #region Action Nodes

        /// <summary>
        /// Determines whether two geometries topologically intersect (share at least one point in common).
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns name="bool">True if the geometries intersect; otherwise, false.</returns>
        public static bool Intersects(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            return a.InternalGeometry.Intersects(b.InternalGeometry);
        }

        /// <summary>
        /// Determines whether geometry 'a' topologically contains geometry 'b'.
        /// </summary>
        /// <param name="a">The container (host) geometry.</param>
        /// <param name="b">The target geometry.</param>
        /// <returns name="bool">True if geometry 'b' lies entirely within geometry 'a'; otherwise, false.</returns>
        public static bool Contains(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            return a.InternalGeometry.Contains(b.InternalGeometry);
        }

        /// <summary>
        /// Determines whether two geometries touch at their boundaries without their interiors overlapping.
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns name="bool">True if the geometries touch at a boundary point; otherwise, false.</returns>
        public static bool Touches(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            return a.InternalGeometry.Touches(b.InternalGeometry);
        }

        /// <summary>
        /// Determines whether two geometries cross each other (their interiors intersect in points of lower dimension).
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns name="bool">True if the geometries cross; otherwise, false.</returns>
        public static bool Crosses(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            return a.InternalGeometry.Crosses(b.InternalGeometry);
        }

        /// <summary>
        /// Determines whether two geometries of the same dimension partially overlap.
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns name="bool">True if the geometries overlap; otherwise, false.</returns>
        public static bool Overlaps(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            return a.InternalGeometry.Overlaps(b.InternalGeometry);
        }

        /// <summary>
        /// Determines whether two geometries are completely disjoint (share no points in common).
        /// </summary>
        /// <param name="a">The first NTS geometry.</param>
        /// <param name="b">The second NTS geometry.</param>
        /// <returns name="bool">True if the geometries are disjoint; otherwise, false.</returns>
        public static bool Disjoint(NtsGeometry a, NtsGeometry b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            return a.InternalGeometry.Disjoint(b.InternalGeometry);
        }

        #endregion
    }
}       