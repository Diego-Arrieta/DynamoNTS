using Autodesk.DesignScript.Runtime;
using DynamoNTS.Features;
using DynamoNTS.Fields;
using DynamoNTS.Geometry;
using DynamoNTS.Layers;
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

namespace DynamoNTS.ImportExport
{
    /// <summary>
    /// Provides methods to read and write ESRI Shapefile datasets (.shp, .dbf, .prj)
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
            [DefaultArgument("null")] CoordinateSystem? crsOverride = null)
        {
            if (string.IsNullOrWhiteSpace(shpPath))
                throw new ArgumentException("Shapefile path cannot be null or empty.", nameof(shpPath));

            if (!File.Exists(shpPath))
                throw new FileNotFoundException($"Shapefile not found: '{shpPath}'");

            // 1. Resolve native source Coordinate Reference System from .prj
            CoordinateSystem? sourceCrs = null;
            string prjPath = Path.ChangeExtension(shpPath, ".prj");
            if (File.Exists(prjPath))
            {
                try
                {
                    sourceCrs = CoordinateSystem.ByPrjFile(prjPath);
                }
                catch
                {
                    sourceCrs = null;
                }
            }

            // 2. Extract schema metadata directly from DBF column headers
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

            // 3. Read all spatial features with their attributes in native coordinates
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

            // 4. Reproject if a new CRS is supplied
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
        /// Writes an NtsLayer to disk as an ESRI Shapefile (.shp, .shx, .dbf, and .prj).
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

            string? dir = Path.GetDirectoryName(shpPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 1. Determine ShapeType based on the layer geometries
            var firstGeom = layer.Features.FirstOrDefault(f => f?.Geometry != null)?.Geometry;
            if (firstGeom == null)
                throw new InvalidOperationException("The layer does not contain valid geometries to export.");

            var shapeType = ResolveShapeType(firstGeom);

            // 2. Build DBF field definitions from the layer's formal schema
            var dbfFields = new List<DbfField>();
            foreach (var field in layer.Schema.Fields)
            {
                dbfFields.Add(CreateDbfField(field));
            }

            // 3. Write features using explicit ShapefileWriterOptions
            var options = new ShapefileWriterOptions(shapeType, dbfFields.ToArray());
            using (var writer = NetTopologySuite.IO.Esri.Shapefile.OpenWrite(shpPath, options))
            {
                foreach (var f in layer.Features)
                {
                    if (f?.Geometry == null) continue;

                    var table = new AttributesTable();
                    foreach (var field in layer.Schema.Fields)
                    {
                        object? val = f.GetValue(field.Name);
                        table.Add(field.Name, (val is DBNull) ? null : val);
                    }

                    writer.Write(new Feature(f.Geometry.InternalGeometry, table));
                }
            }

            // 4. Write projection file (.prj) if CRS is defined
            if (layer.CoordinateSystem?.InternalCRS != null)
            {
                string prjPath = Path.ChangeExtension(shpPath, ".prj");
                File.WriteAllText(prjPath, layer.CoordinateSystem.InternalCRS.WKT, Encoding.UTF8);
            }

            return true;
        }

        #endregion

        #region Helpers

        private static ShapeType ResolveShapeType(NtsGeometry geometry)
        {
            if (geometry is NtsPoint)
                return ShapeType.Point;

            if (geometry is NtsLineString)
                return ShapeType.PolyLine;

            if (geometry is NtsPolygon)
                return ShapeType.Polygon;

            return ShapeType.Point;
        }

        private static NtsFieldType MapFieldType(DbfType type)
        {
            switch (type)
            {
                case DbfType.Numeric:
                case DbfType.Float:
                    return NtsFieldType.Double;
                case DbfType.Date:
                    return NtsFieldType.Date;
                case DbfType.Logical:
                    return NtsFieldType.Boolean;
                default:
                    return NtsFieldType.String;
            }
        }

        private static DbfField CreateDbfField(NtsField field)
        {
            switch (field.Type)
            {
                case NtsFieldType.Integer:
                    return new DbfNumericInt32Field(field.Name);
                case NtsFieldType.Double:
                    return new DbfNumericDoubleField(field.Name);
                case NtsFieldType.Boolean:
                    return new DbfLogicalField(field.Name);
                case NtsFieldType.Date:
                    return new DbfDateField(field.Name);
                default:
                    return new DbfCharacterField(field.Name, field.Length);
            }
        }

        #endregion
    }
}