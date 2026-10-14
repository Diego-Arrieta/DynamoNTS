using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
using DynamoNTS.Geometry;
using DynamoNTS.Projections;
using NetTopologySuite.Features;
using NetTopologySuite.IO.Esri;
using NetTopologySuite.IO.Esri.Dbf;
using NetTopologySuite.IO.Esri.Dbf.Fields;
using NetTopologySuite.IO.Esri.Shapefiles.Writers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DynamoNTS.IO
{
    /// <summary>
    /// Provides methods to read and write ESRI Shapefile datasets (.shp, .dbf, .prj, .cpg)
    /// using the modern NetTopologySuite.IO.Esri engine.
    /// </summary>
    public static class Shapefile
    {
        #region Action Nodes

        /// <summary>
        /// Reads an ESRI Shapefile from disk, preserving its native CRS or reprojecting geometries if a target CRS is provided.
        /// </summary>
        /// <param name="shpPath">Full path to the .shp file.</param>
        /// <param name="crsOverride">Optional target Coordinate Reference System. If provided, layer geometries are reprojected.</param>
        /// <returns name="layer">The loaded (and optionally reprojected) NtsLayer instance.</returns>
        public static NtsLayer Read(
            string shpPath,
            [DefaultArgument("null")] NtsCoordinateSystem? crsOverride = null)
        {
            if (string.IsNullOrWhiteSpace(shpPath))
                throw new ArgumentException("Shapefile path cannot be null or empty.", nameof(shpPath));

            if (!File.Exists(shpPath))
                throw new FileNotFoundException($"Shapefile not found: '{shpPath}'");

            NtsCoordinateSystem? sourceCrs = null;
            string prjPath = Path.ChangeExtension(shpPath, ".prj");
            if (File.Exists(prjPath))
            {
                try
                {
                    sourceCrs = NtsCoordinateSystem.ByPrjFile(prjPath);
                }
                catch
                {
                    sourceCrs = null;
                }
            }

            var fields = new List<NtsField>();
            using (var reader = NetTopologySuite.IO.Esri.Shapefile.OpenRead(shpPath))
            {
                foreach (var dbfField in reader.Fields)
                {
                    var fieldType = MapFieldType(dbfField.FieldType);
                    fields.Add(NtsField.ByNameAndType(dbfField.Name, fieldType, dbfField.Length));
                }
            }
            var schema = NtsSchema.ByFields(fields);

            var rawFeatures = NetTopologySuite.IO.Esri.Shapefile.ReadAllFeatures(shpPath);
            var features = new List<NtsFeature>();

            foreach (var rawFeature in rawFeatures)
            {
                if (rawFeature.Geometry == null || rawFeature.Geometry.IsEmpty) continue;

                var wrappedGeom = NtsGeometry.WrapGeometry(rawFeature.Geometry);
                if (wrappedGeom == null) continue;

                var table = new AttributesTable();
                if (rawFeature.Attributes != null)
                {
                    foreach (var field in schema.Fields)
                    {
                        if (rawFeature.Attributes.Exists(field.Name))
                        {
                            var val = rawFeature.Attributes[field.Name];
                            table.Add(field.Name, (val is DBNull) ? null : val);
                        }
                    }
                }

                var attrs = new NtsRecord(table, schema);
                features.Add(new NtsFeature(rawFeature, wrappedGeom, attrs));
            }

            string layerName = Path.GetFileNameWithoutExtension(shpPath);
            var nativeLayer = NtsLayer.ByFeatures(features, layerName, sourceCrs, schema);

            if (crsOverride != null)
            {
                if (sourceCrs != null)
                {
                    return nativeLayer.Reproject(crsOverride);
                }

                return NtsLayer.ByFeatures(features, layerName, crsOverride, schema);
            }

            return nativeLayer;
        }

        /// <summary>
        /// Writes an NtsLayer to disk as an ESRI Shapefile dataset (.shp, .shx, .dbf, .prj, and .cpg).
        /// Enforces homogeneous geometry constraints and automatically maps field types.
        /// </summary>
        /// <param name="layer">The NtsLayer to export.</param>
        /// <param name="shpPath">Target destination path (.shp).</param>
        /// <returns name="success">True if export succeeded.</returns>
        public static bool Write(NtsLayer layer, string shpPath)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(shpPath))
                throw new ArgumentException("Destination file path cannot be null or empty.", nameof(shpPath));

            if (layer.Count == 0)
                throw new InvalidOperationException("Cannot write an empty layer to Shapefile.");

            if (!layer.IsHomogeneous)
            {
                throw new InvalidOperationException(
                    $"Cannot export layer '{layer.Name}' to Shapefile because it contains mixed geometry types ({layer.GeometryType}). " +
                    "ESRI Shapefiles require all features in a file to have the exact same geometry type. " +
                    "Use 'layer.FilterByGeometryType' to split the layer before exporting.");
            }

            string? dir = Path.GetDirectoryName(shpPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var shapeType = ResolveShapeType(layer);

            var (dbfFields, fieldNameToDbfName) = BuildDbfFieldsMapping(layer.Schema.Fields);

            var options = new ShapefileWriterOptions(shapeType, dbfFields.ToArray())
            {
                Encoding = Encoding.UTF8
            };

            using (var writer = NetTopologySuite.IO.Esri.Shapefile.OpenWrite(shpPath, options))
            {
                foreach (var f in layer.Features)
                {
                    if (f?.Geometry == null) continue;

                    var table = new AttributesTable();
                    foreach (var field in layer.Schema.Fields)
                    {
                        string dbfName = fieldNameToDbfName[field.Name];
                        object? val = f.GetValue(field.Name);

                        if (field.Type == NtsFieldType.String && val is string strVal)
                        {
                            int fieldLen = field.Length > 0 ? field.Length : 50;
                            int maxAllowed = Math.Max(1, Math.Min(254, fieldLen));
                            if (strVal.Length > maxAllowed)
                            {
                                string truncated = strVal.Substring(0, maxAllowed);
                                System.Diagnostics.Trace.TraceWarning(
                                    $"[Shapefile.Write] Campo '{field.Name}' (DBF: '{dbfName}') truncado de {strVal.Length} a {maxAllowed} caracteres. " +
                                    $"Valor: \"{strVal}\" -> \"{truncated}\"");
                                val = truncated;
                            }
                        }

                        table.Add(dbfName, (val is DBNull) ? null : val);
                    }

                    writer.Write(new Feature(f.Geometry.InternalGeometry, table));
                }
            }

            if (layer.CoordinateSystem?.InternalCRS != null)
            {
                string prjPath = Path.ChangeExtension(shpPath, ".prj");
                File.WriteAllText(prjPath, layer.CoordinateSystem.InternalCRS.WKT, Encoding.UTF8);
            }

            string cpgPath = Path.ChangeExtension(shpPath, ".cpg");
            File.WriteAllText(cpgPath, "UTF-8", Encoding.ASCII);

            return true;
        }

        #endregion

        #region Helpers & Disambiguation

        private static ShapeType ResolveShapeType(NtsLayer layer)
        {
            return layer.GeometryType switch
            {
                NtsGeometryType.Point => ShapeType.Point,
                NtsGeometryType.MultiPoint => ShapeType.MultiPoint,
                NtsGeometryType.LineString or NtsGeometryType.MultiLineString => ShapeType.PolyLine,
                NtsGeometryType.Polygon or NtsGeometryType.MultiPolygon => ShapeType.Polygon,
                _ => ShapeType.Point
            };
        }

        private static (List<DbfField> DbfFields, Dictionary<string, string> Mapping) BuildDbfFieldsMapping(IEnumerable<NtsField> fields)
        {
            var dbfFields = new List<DbfField>();
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedDbfNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in fields)
            {
                string sanitized = Regex.Replace(field.Name.Trim(), @"[^a-zA-Z0-9_]", "_");
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    sanitized = "ATTR";
                }

                string candidate = sanitized.Length > 10 ? sanitized.Substring(0, 10) : sanitized;

                if (usedDbfNames.Contains(candidate))
                {
                    int counter = 1;
                    while (true)
                    {
                        string suffix = "_" + counter;
                        int prefixMaxLen = 10 - suffix.Length;
                        string prefix = candidate.Length > prefixMaxLen ? candidate.Substring(0, prefixMaxLen) : candidate;
                        string resolved = prefix + suffix;

                        if (!usedDbfNames.Contains(resolved))
                        {
                            candidate = resolved;
                            break;
                        }
                        counter++;
                    }
                }

                usedDbfNames.Add(candidate);
                mapping[field.Name] = candidate;

                dbfFields.Add(CreateDbfField(candidate, field.Type, field.Length));
            }

            return (dbfFields, mapping);
        }

        private static NtsFieldType MapFieldType(DbfType type)
        {
            return type switch
            {
                DbfType.Numeric or DbfType.Float => NtsFieldType.Double,
                DbfType.Date => NtsFieldType.Date,
                DbfType.Logical => NtsFieldType.Boolean,
                _ => NtsFieldType.String
            };
        }

        private static DbfField CreateDbfField(string dbfName, NtsFieldType type, int length)
        {
            int charLen = length > 0 ? length : 50;
            return type switch
            {
                NtsFieldType.Integer => new DbfNumericInt32Field(dbfName),
                NtsFieldType.Double => new DbfNumericDoubleField(dbfName),
                NtsFieldType.Boolean => new DbfLogicalField(dbfName),
                NtsFieldType.Date => new DbfDateField(dbfName),
                _ => new DbfCharacterField(dbfName, Math.Max(1, Math.Min(254, charLen)))
            };
        }

        #endregion
    }
}