using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
using DynamoNTS.Geometry;
using DynamoNTS.Projections;
using NetTopologySuite.Features;
using NetTopologySuite.Index.Strtree;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DynamoNTS.Data
{
    /// <summary>
    /// Represents an in-memory GIS vector layer containing a unified schema, spatial reference, indexation, and features.
    /// </summary>
    public class NtsLayer
    {
        [IsVisibleInDynamoLibrary(false)]
        internal FeatureCollection InternalCollection { get; }

        [IsVisibleInDynamoLibrary(false)]
        private readonly Lazy<STRtree<NtsFeature>> _spatialIndex;

        [IsVisibleInDynamoLibrary(false)]
        private readonly IReadOnlyList<NtsFeature> _features;

        [IsVisibleInDynamoLibrary(false)]
        private readonly Lazy<bool> _is3D;

        /// <summary>
        /// Acceso interno directo de solo lectura para métodos internos de DynamoNTS.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        internal IReadOnlyList<NtsFeature> InternalFeatures => _features;

        /// <summary>
        /// Gets the layer identifier name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets or sets the origin source of the layer's Coordinate Reference System.
        /// Possible values include: "prj", "override", "unreadable", or "none".
        /// </summary>
        public string CrsSource { get; internal set; } = "none";

        /// <summary>
        /// Gets the formal attribute schema shared across all features in this layer.
        /// </summary>
        public NtsSchema Schema { get; }

        /// <summary>
        /// Gets the Coordinate Reference System (CRS) of the layer, or null if undefined.
        /// </summary>
        public NtsCoordinateSystem? CoordinateSystem { get; }

        /// <summary>
        /// Gets the formal OGC geometry type of the layer, or 'Mixed' if it contains heterogeneous geometries.
        /// </summary>
        public NtsGeometryType GeometryType { get; }

        public bool IsHomogeneous => GeometryType != NtsGeometryType.Mixed && GeometryType != NtsGeometryType.None;

        public bool Is3D => _is3D.Value;

        public int CoordinateDimension => Is3D ? 3 : 2;

        /// <summary>
        /// Gets a defensive copy of the spatial features contained in this layer.
        /// </summary>
        public List<NtsFeature> Features => new List<NtsFeature>(_features);

        /// <summary>
        /// Gets all topological geometries extracted from the features in this layer.
        /// </summary>
        public List<NtsGeometry> Geometries
        {
            get
            {
                return Features
                    .Where(f => f?.Geometry != null)
                    .Select(f => f.Geometry)
                    .ToList();
            }
        }

        /// <summary>
        /// Gets all alphanumeric attribute records extracted from the features in this layer.
        /// </summary>
        public List<NtsRecord> Records
        {
            get
            {
                return Features
                    .Where(f => f?.Record != null)
                    .Select(f => f.Record)
                    .ToList();
            }
        }

        /// <summary>
        /// Gets the total number of features in the layer.
        /// </summary>
        public int Count => Features.Count;

        [IsVisibleInDynamoLibrary(false)]
        internal NtsLayer(
            string name,
            NtsSchema schema,
            IEnumerable<NtsFeature> features,
            NtsCoordinateSystem? crs = null,
            string crsSource = "none")
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Layer" : name;
            Schema = schema ?? NtsSchema.Empty();
            CoordinateSystem = crs;
            CrsSource = crsSource;

            _features = features?.Where(f => f != null).ToList() ?? new List<NtsFeature>();

            InternalCollection = new FeatureCollection();
            foreach (var feature in _features)
            {
                InternalCollection.Add(feature.InternalFeature);
            }

            GeometryType = DetectGeometryType(_features);

            _is3D = new Lazy<bool>(() => _features.Any(f => f?.Geometry != null && f.Geometry.HasZ));

            _spatialIndex = new Lazy<STRtree<NtsFeature>>(() => BuildSpatialIndex(_features));
        }

        #region Create Nodes

        /// <summary>
        /// Creates an NtsLayer from a collection of features. Attribute schema is inferred automatically unless an override is provided.
        /// </summary>
        /// <param name="features">Collection of NtsFeature entities.</param>
        /// <param name="name">Layer identifier name.</param>
        /// <param name="coordinateSystem">Optional Coordinate Reference System (defaults to null / undefined).</param>
        /// <param name="schemaOverride">Optional formal schema to enforce and normalize feature attributes.</param>
        /// <returns name="layer">The constructed NtsLayer.</returns>
        public static NtsLayer ByFeatures(
            IList<NtsFeature> features,
            string name = "Layer",
            [DefaultArgument("null")] NtsCoordinateSystem? coordinateSystem = null,
            [DefaultArgument("null")] NtsSchema? schemaOverride = null)
        {
            if (features == null || features.Count == 0)
                throw new ArgumentException("The feature collection cannot be null or empty.", nameof(features));

            NtsSchema schema = schemaOverride ?? ResolveSchemaFromFeatures(features);

            var normalizedFeatures = new List<NtsFeature>();
            foreach (var feat in features)
            {
                if (feat?.Geometry == null) continue;

                var cleanTable = new AttributesTable();
                foreach (var field in schema.Fields)
                {
                    string canonicalKey = feat.ResolveKey(field.Name);
                    object? rawVal = feat.GetValue(canonicalKey);
                    cleanTable.Add(field.Name, NtsAttribute.CoerceValue(rawVal, field));
                }

                var normalizedRecord = new NtsRecord(cleanTable, schema);
                var normalizedFeature = new NtsFeature(
                    new Feature(feat.Geometry.InternalGeometry, cleanTable),
                    feat.Geometry,
                    normalizedRecord);

                normalizedFeatures.Add(normalizedFeature);
            }

            return new NtsLayer(name, schema, normalizedFeatures, coordinateSystem);
        }

        /// <summary>
        /// Creates an NtsLayer directly from pure geometries with a default incremental FID column.
        /// </summary>
        /// <param name="geometries">Collection of topological geometries.</param>
        /// <param name="name">Layer identifier name.</param>
        /// <param name="coordinateSystem">Optional Coordinate Reference System (defaults to null / undefined).</param>
        /// <returns name="layer">The constructed NtsLayer.</returns>
        public static NtsLayer ByGeometries(
            IList<NtsGeometry> geometries,
            string name = "Layer",
            [DefaultArgument("null")] NtsCoordinateSystem? coordinateSystem = null)
        {
            if (geometries == null || geometries.Count == 0)
                throw new ArgumentException("The geometry collection cannot be null or empty.", nameof(geometries));

            var fidField = NtsField.ByNameAndType("FID", NtsFieldType.Integer);
            var schema = NtsSchema.ByFields(new[] { fidField });
            var featureList = new List<NtsFeature>();

            for (int i = 0; i < geometries.Count; i++)
            {
                var geom = geometries[i];
                if (geom == null) continue;

                var feat = NtsFeature.Create(geom, schema).WithValue(fidField, i + 1);
                featureList.Add(feat);
            }

            return new NtsLayer(name, schema, featureList, coordinateSystem);
        }

        /// <summary>
        /// Initializes an empty formal GIS layer with an assigned schema and optional Coordinate Reference System.
        /// </summary>
        /// <param name="name">Layer identifier name.</param>
        /// <param name="schema">The formal layer schema.</param>
        /// <param name="coordinateSystem">Optional Coordinate Reference System (defaults to null / undefined).</param>
        /// <returns name="layer">An empty NtsLayer instance.</returns>
        public static NtsLayer CreateEmpty(
            string name,
            NtsSchema schema,
            [DefaultArgument("null")] NtsCoordinateSystem? coordinateSystem = null)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            return new NtsLayer(name, schema, Enumerable.Empty<NtsFeature>(), coordinateSystem);
        }

        #endregion

        #region Spatial Indexation & Fast Queries

        /// <summary>
        /// Performs an ultra-fast bounding-box spatial query using an in-memory R-Tree index (STRtree),
        /// then evaluates exact topological intersection against candidate features.
        /// </summary>
        /// <param name="searchGeometry">The query geometry.</param>
        /// <returns name="matchingFeatures">List of features intersecting the search geometry.</returns>
        public List<NtsFeature> QueryIntersects(NtsGeometry searchGeometry)
        {
            if (searchGeometry == null || searchGeometry.InternalGeometry.IsEmpty)
                return new List<NtsFeature>();

            var searchEnvelope = searchGeometry.InternalGeometry.EnvelopeInternal;
            var candidates = _spatialIndex.Value.Query(searchEnvelope);

            var matches = new List<NtsFeature>();
            foreach (var candidate in candidates)
            {
                if (candidate.Geometry.InternalGeometry.Intersects(searchGeometry.InternalGeometry))
                {
                    matches.Add(candidate);
                }
            }

            return matches;
        }

        #endregion

        #region Geometry Filtering & Conversions

        /// <summary>
        /// Extracts a new layer containing only features matching the requested geometry type.
        /// Useful for decomposing mixed layers into homogeneous layers before exporting.
        /// </summary>
        /// <param name="targetType">The geometry type to filter by.</param>
        /// <returns name="layer">A new NtsLayer with filtered features.</returns>
        public NtsLayer FilterByGeometryType(NtsGeometryType targetType)
        {
            var matched = Features
                .Where(f => f?.Geometry != null && f.Geometry.GeometryType == targetType)
                .Select(f => f.Clone()) // Clona cada feature resultante
                .ToList();

            return new NtsLayer($"{Name}_{targetType}", Schema, matched, CoordinateSystem);
        }

        /// <summary>
        /// Produces a copy of the layer where all feature geometries are forced to 2D (clearing Z coordinates).
        /// </summary>
        /// <returns name="layer">A new 2D NtsLayer instance.</returns>
        public NtsLayer Force2D()
        {
            var flatFeatures = new List<NtsFeature>();

            foreach (var feat in Features)
            {
                if (feat?.Geometry == null) continue;

                var flatGeom = feat.Geometry.Force2D();
                if (flatGeom == null) continue;

                // Se clona el record para no compartir la tabla con la capa 3D original
                var clonedRecord = feat.Record.Clone();
                var concreteFeature = new Feature(flatGeom.InternalGeometry, clonedRecord.InternalTable);
                flatFeatures.Add(new NtsFeature(concreteFeature, flatGeom, clonedRecord));
            }

            return new NtsLayer($"{Name}_2D", Schema, flatFeatures, CoordinateSystem);
        }

        #endregion

        #region Schema Migration Nodes

        /// <summary>
        /// Restructures the layer table according to a new schema. Preserves matching columns, drops omitted ones, and sets new ones to null.
        /// </summary>
        /// <param name="newSchema">The target schema to enforce.</param>
        /// <returns name="layer">A new NtsLayer with migrated attributes.</returns>
        public NtsLayer SetSchema(NtsSchema newSchema)
        {
            if (newSchema == null) throw new ArgumentNullException(nameof(newSchema));

            var migratedFeatures = new List<NtsFeature>();
            foreach (var feat in Features)
            {
                var table = new AttributesTable();
                foreach (var field in newSchema.Fields)
                {
                    string canonicalKey = feat.ResolveKey(field.Name);
                    object? existingVal = feat.GetValue(canonicalKey);
                    table.Add(field.Name, NtsAttribute.CoerceValue(existingVal, field));
                }

                var record = new NtsRecord(table, newSchema);
                migratedFeatures.Add(new NtsFeature(
                    new Feature(feat.Geometry.InternalGeometry, table),
                    feat.Geometry,
                    record));
            }

            return new NtsLayer(Name, newSchema, migratedFeatures, CoordinateSystem);
        }

        #endregion

        #region Coordinate System Nodes

        /// <summary>
        /// Assigns or updates the Coordinate Reference System metadata without altering geometric coordinates.
        /// </summary>
        /// <param name="ntsCoordinateSystem">The Coordinate Reference System to assign.</param>
        /// <returns name="layer">A new NtsLayer reference with the assigned CRS.</returns>
        public NtsLayer SetCoordinateSystem(NtsCoordinateSystem ntsCoordinateSystem)
        {
            if (ntsCoordinateSystem == null) throw new ArgumentNullException(nameof(ntsCoordinateSystem));

            var clonedFeatures = Features.Select(f => f.Clone()).ToList();
            return new NtsLayer(Name, Schema, clonedFeatures, ntsCoordinateSystem);
        }

        /// <summary>
        /// Reprojects all geometries in the layer to a target Coordinate Reference System. Requires the layer to have a defined CRS.
        /// Pre-compiles the transformation once for the entire layer to optimize batch execution.
        /// </summary>
        /// <param name="targetCRS">The target Coordinate Reference System.</param>
        /// <returns name="reprojectedLayer">A new NtsLayer with reprojected geometries.</returns>
        public NtsLayer Reproject(NtsCoordinateSystem targetCRS)
        {
            if (CoordinateSystem == null)
            {
                throw new InvalidOperationException("Cannot reproject a layer with an undefined Coordinate Reference System. Assign a CRS first using SetCoordinateSystem.");
            }

            if (targetCRS == null) throw new ArgumentNullException(nameof(targetCRS));

            if (CoordinateSystem.IsEquivalentTo(targetCRS))
            {
                var clonedFeatures = Features.Select(f => f.Clone()).ToList();
                return new NtsLayer(Name, Schema, clonedFeatures, targetCRS, "reprojected");
            }

            // Instancia la transformación matemática y el filtro con buffer reutilizable una única vez para la capa
            var filter = Reprojector.CreateFilter(CoordinateSystem, targetCRS);
            int targetSrid = targetCRS.EPSG;

            var reprojectedFeatures = new List<NtsFeature>(Count);
            foreach (var feature in Features)
            {
                if (feature?.Geometry == null) continue;
                reprojectedFeatures.Add(Reprojector.TransformFeature(feature, filter, targetSrid));
            }

            return new NtsLayer(Name, Schema, reprojectedFeatures, targetCRS, "reprojected");
        }
        #endregion

        #region Action & Query Nodes

        /// <summary>
        /// Returns a new NtsLayer containing the existing features plus the provided collection.
        /// </summary>
        /// <param name="features">Features to append.</param>
        /// <returns name="layer">A new NtsLayer with appended features.</returns>
        public NtsLayer AddFeatures(IList<NtsFeature> features)
        {
            if (features == null || features.Count == 0) return this;

            var combined = new List<NtsFeature>(this.Features);
            combined.AddRange(features);

            return ByFeatures(combined, this.Name, this.CoordinateSystem, this.Schema);
        }

        /// <summary>
        /// Filters layer features by checking equality against an attribute field value.
        /// Utiliza comparación tipada, tolerancia numérica y cultura invariante.
        /// </summary>
        public NtsLayer FilterByAttribute(string fieldName, object? value)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));

            var field = Schema.FieldByName(fieldName);
            object? targetValue = value;
            if (field != null && value != null)
            {
                try
                {
                    targetValue = NtsAttribute.CoerceValue(value, field);
                }
                catch
                {
                    targetValue = value;
                }
            }

            var matched = _features.Where(f =>
                {
                    var val = f.GetValue(fieldName);
                    return AreValuesEqual(val, targetValue);
                })
                .Select(f => f.Clone())
                .ToList();

            return new NtsLayer($"{Name}_Filtered", Schema, matched, CoordinateSystem);
        }

        /// <summary>
        /// Computes the bounding envelope polygon covering all geometries in the layer.
        /// </summary>
        /// <returns name="envelope">An NtsGeometry representing the layer boundary extents.</returns>
        public NtsGeometry? Extents()
        {
            var validGeoms = Features
                .Where(f => f.Geometry?.InternalGeometry != null && !f.Geometry.InternalGeometry.IsEmpty)
                .Select(f => f.Geometry.InternalGeometry)
                .ToList();

            if (validGeoms.Count == 0) return null;

            var env = new NetTopologySuite.Geometries.Envelope();
            foreach (var geom in validGeoms)
            {
                env.ExpandToInclude(geom.EnvelopeInternal);
            }

            var factory = NetTopologySuite.Geometries.GeometryFactory.Default;
            var poly = factory.ToGeometry(env);

            return NtsGeometry.WrapGeometry(poly);
        }

        /// <summary>
        /// Provides a structured spatial summary dictionary of the layer, ideal for inspection in Dynamo.
        /// </summary>
        /// <returns name="summary">Dictionary of layer metadata attributes.</returns>
        public Dictionary<string, object> SpatialSummary()
        {
            var env = Extents()?.InternalGeometry?.EnvelopeInternal;

            return new Dictionary<string, object>
            {
                { "Name", Name },
                { "FeatureCount", Count },
                { "GeometryType", GeometryType.ToString() },
                { "IsHomogeneous", IsHomogeneous },
                { "Dimension", Is3D ? "3D (XYZ)" : "2D (XY)" },
                { "EPSG", CoordinateSystem?.EPSG ?? 0 },
                { "CRS", CoordinateSystem?.Name ?? "Undefined" },
                { "FieldCount", Schema.FieldCount },
                { "XMin", env?.MinX ?? 0.0 },
                { "YMin", env?.MinY ?? 0.0 },
                { "XMax", env?.MaxX ?? 0.0 },
                { "YMax", env?.MaxY ?? 0.0 }
            };
        }

        /// <summary>
        /// Extracts all values of a specified attribute column as a flat list directly from the layer.
        /// Eliminates the need for complex lacing or List.Map operations in Dynamo.
        /// </summary>
        /// <param name="fieldName">The target column/field name.</param>
        /// <returns name="values">Flat list of attribute values for that column.</returns>
        public List<object?> GetColumnValues(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));

            return Features
                .Select(f => f?.GetValue(fieldName))
                .ToList();
        }

        /// <summary>
        /// Converts all attribute records in the layer into a list of native Dynamo dictionaries.
        /// Ideal for seamless interoperability with Data-Shapes, CivilConnection, Excel, or CSV export nodes.
        /// </summary>
        /// <returns name="dictionaries">List of dictionaries representing each feature's attributes.</returns>
        public List<Dictionary<string, object?>> ToDictionaries()
        {
            return Features
                .Select(f => f?.Record?.ToDictionary() ?? new Dictionary<string, object?>())
                .ToList();
        }

        #endregion

        #region Helpers

        private static NtsGeometryType DetectGeometryType(IEnumerable<NtsFeature> features)
        {
            bool hasPoint = false, hasMultiPoint = false;
            bool hasLine = false, hasMultiLine = false;
            bool hasPoly = false, hasMultiPoly = false;

            foreach (var f in features)
            {
                if (f?.Geometry == null || f.Geometry.InternalGeometry.IsEmpty) continue;

                switch (f.Geometry.GeometryType)
                {
                    case NtsGeometryType.Point:
                        hasPoint = true;
                        break;
                    case NtsGeometryType.MultiPoint:
                        hasMultiPoint = true;
                        break;
                    case NtsGeometryType.LineString:
                        hasLine = true;
                        break;
                    case NtsGeometryType.MultiLineString:
                        hasMultiLine = true;
                        break;
                    case NtsGeometryType.Polygon:
                        hasPoly = true;
                        break;
                    case NtsGeometryType.MultiPolygon:
                        hasMultiPoly = true;
                        break;
                    default:
                        return NtsGeometryType.Mixed;
                }

                int dimensions = (hasPoint || hasMultiPoint ? 1 : 0)
                                 + (hasLine || hasMultiLine ? 1 : 0)
                                 + (hasPoly || hasMultiPoly ? 1 : 0);

                if (dimensions > 1)
                {
                    return NtsGeometryType.Mixed;
                }
            }

            if (hasPoly || hasMultiPoly)
                return hasMultiPoly ? NtsGeometryType.MultiPolygon : NtsGeometryType.Polygon;

            if (hasLine || hasMultiLine)
                return hasMultiLine ? NtsGeometryType.MultiLineString : NtsGeometryType.LineString;

            if (hasPoint || hasMultiPoint)
                return hasMultiPoint ? NtsGeometryType.MultiPoint : NtsGeometryType.Point;

            return NtsGeometryType.None;
        }

        private static STRtree<NtsFeature> BuildSpatialIndex(IEnumerable<NtsFeature> features)
        {
            var tree = new STRtree<NtsFeature>();

            foreach (var feat in features)
            {
                if (feat?.Geometry?.InternalGeometry != null && !feat.Geometry.InternalGeometry.IsEmpty)
                {
                    tree.Insert(feat.Geometry.InternalGeometry.EnvelopeInternal, feat);
                }
            }

            tree.Build();
            return tree;
        }

        private static NtsSchema ResolveSchemaFromFeatures(IList<NtsFeature> features)
        {
            if (features == null || features.Count == 0)
                return NtsSchema.Empty();

            var fieldNames = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in features)
            {
                if (f?.Record == null) continue;
                foreach (var field in f.Record.Fields)
                {
                    if (seen.Add(field.Name))
                    {
                        fieldNames.Add(field.Name);
                    }
                }
            }

            if (fieldNames.Count == 0)
                return NtsSchema.Empty();

            var inferredFields = new List<NtsField>();
            foreach (var name in fieldNames)
            {
                var colValues = features.Select(f => f?.GetValue(name));
                inferredFields.Add(NtsField.InferField(name, colValues));
            }

            return NtsSchema.ByFields(inferredFields);
        }

        #endregion

        #region Value Comparison Helpers

        private static bool AreValuesEqual(object? a, object? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            if (IsNumeric(a) && IsNumeric(b))
            {
                try
                {
                    double numA = Convert.ToDouble(a, CultureInfo.InvariantCulture);
                    double numB = Convert.ToDouble(b, CultureInfo.InvariantCulture);
                    return Math.Abs(numA - numB) < 1e-9;
                }
                catch
                {
                    // En caso de desbordamiento extremo
                }
            }

            if (a is DateTime dtA && b is DateTime dtB)
            {
                return dtA == dtB;
            }

            if (a is bool boolA && b is bool boolB)
            {
                return boolA == boolB;
            }

            if (a.GetType() == b.GetType() && a is IComparable comp)
            {
                return comp.CompareTo(b) == 0;
            }

            string sa = Convert.ToString(a, CultureInfo.InvariantCulture) ?? string.Empty;
            string sb = Convert.ToString(b, CultureInfo.InvariantCulture) ?? string.Empty;

            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumeric(object value)
        {
            return value is byte or sbyte
                or short or ushort
                or int or uint
                or long or ulong
                or float or double or decimal;
        }

        #endregion

        [IsVisibleInDynamoLibrary(false)]
        public override string ToString()
        {
            string crsText = CoordinateSystem != null ? (CoordinateSystem.EPSG != 0 ? $"EPSG:{CoordinateSystem.EPSG}" : CoordinateSystem.Name) : "Undefined";
            string dimText = Is3D ? "3D" : "2D";
            return $"NtsLayer(Name='{Name}', Features={Count}, Type={GeometryType}, Dim={dimText}, Fields={Schema.FieldCount}, CRS={crsText})";
        }
    }
}