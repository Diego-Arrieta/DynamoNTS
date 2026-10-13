using Autodesk.DesignScript.Runtime;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Represents standard OGC topological geometry types supported across DynamoNTS layers and features.
    /// </summary>
    public enum NtsGeometryType
    {
        /// <summary>
        /// Indicates an empty layer, uninitialized state, or a layer without geometry columns.
        /// </summary>
        None,

        /// <summary>
        /// Single 0-dimensional point entity (X, Y, optional Z).
        /// </summary>
        Point,

        /// <summary>
        /// Single 1-dimensional open or closed curve / polyline.
        /// </summary>
        LineString,

        /// <summary>
        /// Single 2-dimensional planar surface with an exterior ring and optional interior holes.
        /// </summary>
        Polygon,

        /// <summary>
        /// Collection of homogeneous point entities.
        /// </summary>
        MultiPoint,

        /// <summary>
        /// Collection of homogeneous polyline / curve entities.
        /// </summary>
        MultiLineString,

        /// <summary>
        /// Collection of homogeneous planar polygon entities.
        /// </summary>
        MultiPolygon,

        /// <summary>
        /// Heterogeneous or mixed collection containing different geometry types in the same layer.
        /// </summary>
        Mixed
    }
}