using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
using DynamoNTS.Geometry;
using DynamoNTS.Projections;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DynamoNTS.IO
{
    /// <summary>
    /// Provides methods to import and export OGC KML (Keyhole Markup Language) datasets.
    /// KML natively operates in WGS84 geographic coordinates (EPSG:4326).
    /// </summary>
    public static class KML
    {
        private static readonly XNamespace KmlNs = "http://www.opengis.net/kml/2.2";

        #region Action Nodes

        /// <summary>
        /// Reads a KML file from disk and parses its Placemarks into an NtsLayer with WGS84 CRS.
        /// Supports typed Schema definitions, SimpleData, and recursive geometry hierarchies.
        /// </summary>
        /// <param name="filePath">Path to the .kml file.</param>
        /// <param name="targetCrs">Optional target Coordinate Reference System to reproject the data into.</param>
        /// <returns name="layer">The loaded NtsLayer instance.</returns>
        public static NtsLayer Read(
            string filePath,
            [DefaultArgument("null")] NtsCoordinateSystem? targetCrs = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("KML file path cannot be null or empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"KML file not found: '{filePath}'");

            var doc = XDocument.Load(filePath);
            var placemarks = doc.Descendants().Where(e => e.Name.LocalName == "Placemark").ToList();

            if (placemarks.Count == 0)
                throw new InvalidOperationException("No Placemarks were found in the provided KML file.");

            var schemaTypeMap = ParseSchemaTypeDefinitions(doc);

            var features = new List<NtsFeature>();
            var fieldNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var geomFactory = GeometryFactory.Default;

            foreach (var placemark in placemarks)
            {
                var ntsGeom = ParseGeometryNode(placemark, geomFactory);
                if (ntsGeom == null || ntsGeom.IsEmpty) continue;

                var wrappedGeom = NtsGeometry.WrapGeometry(ntsGeom);
                if (wrappedGeom == null) continue;

                var rawAttrs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                var nameElem = placemark.Elements().FirstOrDefault(e => e.Name.LocalName == "name");
                if (nameElem != null && !string.IsNullOrWhiteSpace(nameElem.Value))
                {
                    rawAttrs["Name"] = nameElem.Value;
                }

                var descElem = placemark.Elements().FirstOrDefault(e => e.Name.LocalName == "description");
                if (descElem != null && !string.IsNullOrWhiteSpace(descElem.Value))
                {
                    rawAttrs["Description"] = descElem.Value;
                }

                var extendedData = placemark.Elements().FirstOrDefault(e => e.Name.LocalName == "ExtendedData");
                if (extendedData != null)
                {
                    foreach (var data in extendedData.Elements().Where(e => e.Name.LocalName == "Data"))
                    {
                        string? attrName = data.Attribute("name")?.Value;
                        string? attrVal = data.Elements().FirstOrDefault(e => e.Name.LocalName == "value")?.Value;
                        if (!string.IsNullOrEmpty(attrName))
                        {
                            rawAttrs[attrName] = CoerceKmlValue(attrVal, attrName, schemaTypeMap);
                        }
                    }

                    foreach (var schemaData in extendedData.Elements().Where(e => e.Name.LocalName == "SchemaData"))
                    {
                        foreach (var simpleData in schemaData.Elements().Where(e => e.Name.LocalName == "SimpleData"))
                        {
                            string? attrName = simpleData.Attribute("name")?.Value;
                            string? attrVal = simpleData.Value;
                            if (!string.IsNullOrEmpty(attrName))
                            {
                                rawAttrs[attrName] = CoerceKmlValue(attrVal, attrName, schemaTypeMap);
                            }
                        }
                    }
                }

                foreach (var key in rawAttrs.Keys)
                {
                    if (seenNames.Add(key))
                    {
                        fieldNames.Add(key);
                    }
                }

                var record = rawAttrs.Count > 0 ? NtsRecord.FromDictionary(rawAttrs) : NtsRecord.Empty();
                features.Add(NtsFeature.ByRecord(wrappedGeom, record));
            }

            if (features.Count == 0)
                throw new InvalidOperationException("No valid geometries could be parsed from the KML Placemarks.");

            var inferredFields = new List<NtsField>();
            foreach (var name in fieldNames)
            {
                var values = features.Select(feat => feat.GetValue(name));
                inferredFields.Add(NtsField.InferField(name, values));
            }

            var schema = inferredFields.Count > 0
                ? NtsSchema.ByFields(inferredFields)
                : NtsSchema.Empty();

            string layerName = Path.GetFileNameWithoutExtension(filePath);
            var layer = NtsLayer.ByFeatures(features, layerName, NtsCoordinateSystem.WGS84, schema);

            if (targetCrs != null && !targetCrs.IsEquivalentTo(NtsCoordinateSystem.WGS84))
            {
                return layer.Reproject(targetCrs);
            }

            return layer;
        }

        /// <summary>
        /// Writes an NtsLayer to disk as a KML 2.2 document with typed Schema and ISO 8601 timestamps.
        /// Reprojects to WGS84 if a different CRS is defined, or validates geographic coordinate bounds if CRS is undefined.
        /// </summary>
        /// <param name="layer">The NtsLayer to export.</param>
        /// <param name="filePath">Target destination path (.kml).</param>
        /// <returns name="success">True if export succeeded.</returns>
        public static bool Write(NtsLayer layer, string filePath)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Destination path cannot be null or empty.", nameof(filePath));

            if (layer.Count == 0)
                throw new InvalidOperationException("Cannot write an empty layer to KML.");

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            NtsLayer exportLayer = layer;
            if (layer.CoordinateSystem != null)
            {
                if (!layer.CoordinateSystem.IsEquivalentTo(NtsCoordinateSystem.WGS84))
                {
                    exportLayer = layer.Reproject(NtsCoordinateSystem.WGS84);
                }
            }
            else
            {
                ValidateGeographicBounds(layer, "KML");
            }

            var documentElem = new XElement(KmlNs + "Document",
                new XElement(KmlNs + "name", exportLayer.Name));

            string schemaId = $"{exportLayer.Name}_Schema";
            var schemaFields = exportLayer.Schema.Fields
                .Where(f => !f.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.Equals("Description", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (schemaFields.Count > 0)
            {
                var schemaElem = new XElement(KmlNs + "Schema",
                    new XAttribute("name", exportLayer.Name),
                    new XAttribute("id", schemaId));

                foreach (var field in schemaFields)
                {
                    string kmlType = MapFieldTypeToKmlType(field.Type);
                    schemaElem.Add(new XElement(KmlNs + "SimpleField",
                        new XAttribute("name", field.Name),
                        new XAttribute("type", kmlType)));
                }

                documentElem.Add(schemaElem);
            }

            foreach (var feature in exportLayer.Features)
            {
                if (feature?.Geometry == null) continue;

                var placemark = new XElement(KmlNs + "Placemark");

                object? nameVal = feature.GetValue("Name") ?? feature.GetValue("name");
                if (nameVal != null)
                {
                    placemark.Add(new XElement(KmlNs + "name", nameVal.ToString()));
                }

                object? descVal = feature.GetValue("Description") ?? feature.GetValue("description");
                if (descVal != null)
                {
                    placemark.Add(new XElement(KmlNs + "description", descVal.ToString()));
                }

                if (schemaFields.Count > 0)
                {
                    var extendedData = new XElement(KmlNs + "ExtendedData");
                    var schemaData = new XElement(KmlNs + "SchemaData",
                        new XAttribute("schemaUrl", $"#{schemaId}"));

                    foreach (var field in schemaFields)
                    {
                        object? val = feature.GetValue(field.Name);
                        if (val != null)
                        {
                            string formattedVal = FormatKmlValue(val, field.Type);
                            schemaData.Add(new XElement(KmlNs + "SimpleData",
                                new XAttribute("name", field.Name),
                                formattedVal));
                        }
                    }

                    if (schemaData.HasElements)
                    {
                        extendedData.Add(schemaData);
                        placemark.Add(extendedData);
                    }
                }

                var geomElem = ConvertGeometryToKml(feature.Geometry.InternalGeometry);
                if (geomElem != null)
                {
                    placemark.Add(geomElem);
                    documentElem.Add(placemark);
                }
            }

            var kmlRoot = new XElement(KmlNs + "kml", documentElem);
            var xDoc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), kmlRoot);
            xDoc.Save(filePath);

            return true;
        }

        #endregion

        #region Geometry Parsing (B34: Recursive Parser)

        /// <summary>
        /// Recursively parses geometries from direct child elements without using broad descendant queries.
        /// </summary>
        private static NetTopologySuite.Geometries.Geometry? ParseGeometryNode(XElement parent, GeometryFactory factory)
        {
            var geoms = new List<NetTopologySuite.Geometries.Geometry>();

            foreach (var child in parent.Elements())
            {
                string localName = child.Name.LocalName;

                if (localName.Equals("MultiGeometry", StringComparison.OrdinalIgnoreCase))
                {
                    var subGeom = ParseGeometryNode(child, factory);
                    if (subGeom != null && !subGeom.IsEmpty)
                    {
                        geoms.Add(subGeom);
                    }
                }
                else if (localName.Equals("Point", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = ParsePointElement(child, factory);
                    if (parsed != null) geoms.Add(parsed);
                }
                else if (localName.Equals("LineString", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = ParseLineStringElement(child, factory);
                    if (parsed != null) geoms.Add(parsed);
                }
                else if (localName.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = ParsePolygonElement(child, factory);
                    if (parsed != null) geoms.Add(parsed);
                }
            }

            if (geoms.Count == 0) return null;
            if (geoms.Count == 1) return geoms[0];

            return factory.BuildGeometry(geoms);
        }

        private static NetTopologySuite.Geometries.Point? ParsePointElement(XElement pointElem, GeometryFactory factory)
        {
            var coordsElem = pointElem.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
            var coords = ParseCoordinates(coordsElem?.Value);
            return coords.Length > 0 ? factory.CreatePoint(coords[0]) : null;
        }

        private static NetTopologySuite.Geometries.LineString? ParseLineStringElement(XElement lineElem, GeometryFactory factory)
        {
            var coordsElem = lineElem.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
            var coords = ParseCoordinates(coordsElem?.Value);
            return coords.Length >= 2 ? factory.CreateLineString(coords) : null;
        }

        private static NetTopologySuite.Geometries.Polygon? ParsePolygonElement(XElement polyElem, GeometryFactory factory)
        {
            var outerElem = polyElem.Elements().FirstOrDefault(e => e.Name.LocalName == "outerBoundaryIs");
            var outerCoordsElem = outerElem?.Elements().FirstOrDefault(e => e.Name.LocalName == "LinearRing")
                                           ?.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates")
                                 ?? outerElem?.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates");

            var outerCoords = ParseCoordinates(outerCoordsElem?.Value);
            if (outerCoords.Length < 3) return null;

            var shell = CreateClosedRing(outerCoords, factory);

            var holes = new List<LinearRing>();
            var inners = polyElem.Elements().Where(e => e.Name.LocalName == "innerBoundaryIs");
            foreach (var inner in inners)
            {
                var innerCoordsElem = inner.Elements().FirstOrDefault(e => e.Name.LocalName == "LinearRing")
                                          ?.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates")
                                     ?? inner.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates");

                var innerCoords = ParseCoordinates(innerCoordsElem?.Value);
                if (innerCoords.Length >= 3)
                {
                    holes.Add(CreateClosedRing(innerCoords, factory));
                }
            }

            return factory.CreatePolygon(shell, holes.ToArray());
        }

        private static LinearRing CreateClosedRing(Coordinate[] coords, GeometryFactory factory)
        {
            var list = new List<Coordinate>(coords);
            var first = list[0];
            var last = list[list.Count - 1];

            if (first.X != last.X || first.Y != last.Y || (!double.IsNaN(first.Z) && first.Z != last.Z))
            {
                list.Add(new CoordinateZ(first.X, first.Y, first.Z));
            }

            return factory.CreateLinearRing(list.ToArray());
        }

        private static Coordinate[] ParseCoordinates(string? coordString)
        {
            if (string.IsNullOrWhiteSpace(coordString)) return Array.Empty<Coordinate>();

            var coordinates = new List<Coordinate>();
            var tuples = coordString.Trim().Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var tuple in tuples)
            {
                var parts = tuple.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                {
                    double z = (parts.Length >= 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double alt))
                        ? alt
                        : double.NaN;

                    coordinates.Add(double.IsNaN(z) ? new Coordinate(lon, lat) : new CoordinateZ(lon, lat, z));
                }
            }

            return coordinates.ToArray();
        }

        #endregion

        #region Serialization Helpers

        private static XElement? ConvertGeometryToKml(NetTopologySuite.Geometries.Geometry geometry)
        {
            if (geometry is NetTopologySuite.Geometries.Point p)
            {
                return new XElement(KmlNs + "Point",
                    new XElement(KmlNs + "coordinates", FormatCoord(p.Coordinate)));
            }

            if (geometry is NetTopologySuite.Geometries.LineString ls)
            {
                string coords = string.Join(" ", ls.Coordinates.Select(FormatCoord));
                return new XElement(KmlNs + "LineString",
                    new XElement(KmlNs + "coordinates", coords));
            }

            if (geometry is NetTopologySuite.Geometries.Polygon poly)
            {
                var polyElem = new XElement(KmlNs + "Polygon",
                    new XElement(KmlNs + "outerBoundaryIs",
                        new XElement(KmlNs + "LinearRing",
                            new XElement(KmlNs + "coordinates", string.Join(" ", poly.ExteriorRing.Coordinates.Select(FormatCoord))))));

                for (int i = 0; i < poly.NumInteriorRings; i++)
                {
                    polyElem.Add(new XElement(KmlNs + "innerBoundaryIs",
                        new XElement(KmlNs + "LinearRing",
                            new XElement(KmlNs + "coordinates", string.Join(" ", poly.GetInteriorRingN(i).Coordinates.Select(FormatCoord))))));
                }

                return polyElem;
            }

            if (geometry is NetTopologySuite.Geometries.MultiPoint mp)
            {
                var multiElem = new XElement(KmlNs + "MultiGeometry");
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    multiElem.Add(ConvertGeometryToKml(mp.GetGeometryN(i)));
                }
                return multiElem;
            }

            if (geometry is NetTopologySuite.Geometries.MultiLineString mls)
            {
                var multiElem = new XElement(KmlNs + "MultiGeometry");
                for (int i = 0; i < mls.NumGeometries; i++)
                {
                    multiElem.Add(ConvertGeometryToKml(mls.GetGeometryN(i)));
                }
                return multiElem;
            }

            if (geometry is NetTopologySuite.Geometries.MultiPolygon mpoly)
            {
                var multiElem = new XElement(KmlNs + "MultiGeometry");
                for (int i = 0; i < mpoly.NumGeometries; i++)
                {
                    multiElem.Add(ConvertGeometryToKml(mpoly.GetGeometryN(i)));
                }
                return multiElem;
            }

            return null;
        }

        private static string FormatCoord(Coordinate? c)
        {
            if (c == null) return string.Empty;
            if (!double.IsNaN(c.Z))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", c.X, c.Y, c.Z);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0},{1}", c.X, c.Y);
        }

        #endregion

        #region Schema and Type Helpers (B32, B33)

        private static Dictionary<string, string> ParseSchemaTypeDefinitions(XDocument doc)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var schemas = doc.Descendants().Where(e => e.Name.LocalName == "Schema");
            foreach (var schema in schemas)
            {
                foreach (var field in schema.Elements().Where(e => e.Name.LocalName == "SimpleField"))
                {
                    string? name = field.Attribute("name")?.Value;
                    string? type = field.Attribute("type")?.Value;

                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(type))
                    {
                        dict[name] = type.ToLowerInvariant();
                    }
                }
            }

            return dict;
        }

        private static object? CoerceKmlValue(string? rawVal, string fieldName, Dictionary<string, string> schemaTypes)
        {
            if (rawVal == null) return null;

            if (schemaTypes.TryGetValue(fieldName, out string? declaredType))
            {
                switch (declaredType)
                {
                    case "int":
                    case "uint":
                    case "short":
                    case "ushort":
                        if (int.TryParse(rawVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                            return intVal;
                        break;

                    case "float":
                    case "double":
                        if (double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                            return dblVal;
                        break;

                    case "bool":
                        if (bool.TryParse(rawVal, out bool boolVal))
                            return boolVal;
                        if (rawVal == "1") return true;
                        if (rawVal == "0") return false;
                        break;

                    case "string":
                        return rawVal;
                }
            }

            if (int.TryParse(rawVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inferredInt))
                return inferredInt;

            if (double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double inferredDouble))
                return inferredDouble;

            if (bool.TryParse(rawVal, out bool inferredBool))
                return inferredBool;

            if (DateTime.TryParse(rawVal, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsedDt))
                return parsedDt;

            return rawVal;
        }

        private static string MapFieldTypeToKmlType(NtsFieldType type)
        {
            return type switch
            {
                NtsFieldType.Integer => "int",
                NtsFieldType.Double => "double",
                NtsFieldType.Boolean => "bool",
                _ => "string"
            };
        }

        private static string FormatKmlValue(object val, NtsFieldType type)
        {
            if (val is DateTime dt)
            {
                return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            }

            if (val is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return val.ToString() ?? string.Empty;
        }

        private static void ValidateGeographicBounds(NtsLayer layer, string format)
        {
            var env = layer.Extents()?.InternalGeometry?.EnvelopeInternal;
            if (env == null || env.IsNull) return;

            if (env.MinX < -180.0 || env.MaxX > 180.0 || env.MinY < -90.0 || env.MaxY > 90.0)
            {
                throw new InvalidOperationException(
                    $"Cannot export layer '{layer.Name}' to {format} because it has no Coordinate Reference System (CRS) defined " +
                    $"and its coordinates fall outside valid WGS84 geographic limits (Longitude: [-180, 180], Latitude: [-90, 90]). " +
                    $"Detected bounds: [X: {env.MinX:F2}..{env.MaxX:F2}, Y: {env.MinY:F2}..{env.MaxY:F2}]. " +
                    "The data appears to use projected coordinates (e.g., UTM). Assign a CRS before exporting.");
            }
        }

        #endregion
    }
}