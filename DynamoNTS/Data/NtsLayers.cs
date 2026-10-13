using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
using DynamoNTS.Geometry;
using DynamoNTS.Projections;
using NetTopologySuite.Features;
using NetTopologySuite.Index.Strtree;
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Gets the layer identifier name.
        /// </summary>
        public string Name { get; }

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

        /// <summary>
        /// Indicates whether all features in this layer share the same geometry type.
        /// Essential for validating export compatibility with formats like ESRI Shapefile.
        /// </summary>
        public bool IsHomogeneous => GeometryType != NtsGeometryType.Mixed && GeometryType != NtsGeometryType.None;

        /// <summary>
        /// Indicates whether the layer contains valid 3D elevation coordinates (Z).
        /// </summary>
        public bool Is3D { get; }

        /// <summary>
        /// Gets the coordinate dimension: 3 for 3D coordinates (XYZ), otherwise 2 (XY).
        /// </summary>
        public int CoordinateDimension => Is3D ? 3 : 2;

        /// <summary>
        /// Gets the list of spatial features contained in this layer.
        /// </summary>
        public List<NtsFeature> Features { get; }

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
            NtsCoordinateSystem? crs = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Layer" : name;
            Schema = schema ?? NtsSchema.Empty();
            CoordinateSystem = crs;
            Features = features?.Where(f => f != null).ToList() ?? new List<NtsFeature>();

            InternalCollection = new FeatureCollection();
            foreach (var feature in Features)
            {
                InternalCollection.Add(feature.InternalFeature);
            }

            // Detección automática de metadatos espaciales
            GeometryType = DetectGeometryType(Features);
            Is3D = Features.Any(f => f?.Geometry != null && f.Geometry.HasZ);

            // Inicialización diferida del índice espacial R-Tree (STRtree)
            _spatialIndex = new Lazy<STRtree<NtsFeature>>(() => BuildSpatialIndex(Features));
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
                    object? rawVal = feat.GetValue(field.Name);
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

                var feat = NtsFeature.Create(geom, schema);
                feat.SetValue(fidField, i + 1);
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

                var concreteFeature = new Feature(flatGeom.InternalGeometry, feat.Record.InternalTable);
                flatFeatures.Add(new NtsFeature(concreteFeature, flatGeom, feat.Record));
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
                    object? existingVal = feat.GetValue(field.Name);
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
            return new NtsLayer(Name, Schema, Features, ntsCoordinateSystem);
        }

        /// <summary>
        /// Reprojects all geometries in the layer to a target Coordinate Reference System. Requires the layer to have a defined CRS.
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

            var reprojectedFeatures = new List<NtsFeature>();
            foreach (var feature in Features)
            {
                reprojectedFeatures.Add(Reprojector.TransformFeature(feature, CoordinateSystem, targetCRS));
            }

            return new NtsLayer(Name, Schema, reprojectedFeatures, targetCRS);
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
        /// </summary>
        /// <param name="fieldName">Field column name.</param>
        /// <param name="value">Target value to match.</param>
        /// <returns name="filteredLayer">A new NtsLayer containing only matching features.</returns>
        public NtsLayer FilterByAttribute(string fieldName, object? value)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));

            var matched = Features.Where(f =>
            {
                var val = f.GetValue(fieldName);
                if (val == null && value == null) return true;
                if (val == null || value == null) return false;
                return val.ToString()!.Equals(value.ToString(), StringComparison.OrdinalIgnoreCase);
            }).ToList();

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
            NtsGeometryType? detected = null;

            foreach (var f in features)
            {
                if (f?.Geometry == null || f.Geometry.InternalGeometry.IsEmpty) continue;

                var current = f.Geometry.GeometryType;
                if (current == NtsGeometryType.None) continue;

                if (detected == null)
                {
                    detected = current;
                }
                else if (detected.Value != current)
                {
                    return NtsGeometryType.Mixed;
                }
            }

            return detected ?? NtsGeometryType.None;
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
            var fieldsMap = new Dictionary<string, NtsField>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in features)
            {
                if (f?.Record == null) continue;
                foreach (var field in f.Record.Fields)
                {
                    if (!fieldsMap.ContainsKey(field.Name))
                    {
                        fieldsMap[field.Name] = field;
                    }
                }
            }

            return fieldsMap.Count > 0
                ? NtsSchema.ByFields(fieldsMap.Values.ToList())
                : NtsSchema.Empty();
        }

        #endregion

        public override string ToString()
        {
            string crsText = CoordinateSystem != null ? (CoordinateSystem.EPSG != 0 ? $"EPSG:{CoordinateSystem.EPSG}" : CoordinateSystem.Name) : "Undefined";
            string dimText = Is3D ? "3D" : "2D";
            return $"NtsLayer(Name='{Name}', Features={Count}, Type={GeometryType}, Dim={dimText}, Fields={Schema.FieldCount}, CRS={crsText})";
        }
    }
}