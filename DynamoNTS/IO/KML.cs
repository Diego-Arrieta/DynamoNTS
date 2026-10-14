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

            var features = new List<NtsFeature>();
            var fieldNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var geomFactory = GeometryFactory.Default;

            foreach (var placemark in placemarks)
            {
                // 1. Parse Geometry (including MultiGeometry and polygons with holes)
                var ntsGeom = ParseGeometry(placemark, geomFactory);
                if (ntsGeom == null || ntsGeom.IsEmpty) continue;

                var wrappedGeom = NtsGeometry.WrapGeometry(ntsGeom);
                if (wrappedGeom == null) continue;

                // 2. Parse Attributes (Name, Description, ExtendedData)
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
                            rawAttrs[attrName] = attrVal;
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

            if (targetCrs != null && targetCrs.EPSG != NtsCoordinateSystem.WGS84.EPSG)
            {
                return layer.Reproject(targetCrs);
            }

            return layer;
        }

        /// <summary>
        /// Writes an NtsLayer to disk as a KML 2.2 document. Reprojects to WGS84 if a different CRS is defined.
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
            if (layer.CoordinateSystem != null && layer.CoordinateSystem.EPSG != 4326)
            {
                exportLayer = layer.Reproject(NtsCoordinateSystem.WGS84);
            }

            var documentElem = new XElement(KmlNs + "Document",
                new XElement(KmlNs + "name", exportLayer.Name));

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

                if (exportLayer.Schema.FieldCount > 0)
                {
                    var extendedData = new XElement(KmlNs + "ExtendedData");
                    foreach (var field in exportLayer.Schema.Fields)
                    {
                        if (field.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                            field.Name.Equals("Description", StringComparison.OrdinalIgnoreCase))
                            continue;

                        object? val = feature.GetValue(field.Name);
                        if (val != null)
                        {
                            extendedData.Add(new XElement(KmlNs + "Data",
                                new XAttribute("name", field.Name),
                                new XElement(KmlNs + "value", Convert.ToString(val, CultureInfo.InvariantCulture))));
                        }
                    }

                    if (extendedData.HasElements)
                    {
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

        #region Internal Helpers

        private static NetTopologySuite.Geometries.Geometry? ParseGeometry(XElement element, GeometryFactory factory)
        {
            // 1. MultiGeometry
            var multiElem = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "MultiGeometry");
            if (multiElem != null)
            {
                var children = new List<NetTopologySuite.Geometries.Geometry>();
                foreach (var child in multiElem.Elements())
                {
                    var parsed = ParseSingleGeometry(child, factory);
                    if (parsed != null && !parsed.IsEmpty)
                    {
                        children.Add(parsed);
                    }
                }

                if (children.Count > 0)
                {
                    return factory.BuildGeometry(children);
                }
            }

            // 2. Direct individual geometry
            return ParseSingleGeometry(element, factory);
        }

        private static NetTopologySuite.Geometries.Geometry? ParseSingleGeometry(XElement elem, GeometryFactory factory)
        {
            // Point
            var pointElem = elem.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "Point");
            if (pointElem != null)
            {
                var coordsElem = pointElem.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                var coords = ParseCoordinates(coordsElem?.Value);
                if (coords.Length > 0) return factory.CreatePoint(coords[0]);
            }

            // LineString
            var lineElem = elem.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "LineString");
            if (lineElem != null)
            {
                var coordsElem = lineElem.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                var coords = ParseCoordinates(coordsElem?.Value);
                if (coords.Length >= 2) return factory.CreateLineString(coords);
            }

            // Polygon (supporting innerBoundaryIs)
            var polyElem = elem.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "Polygon");
            if (polyElem != null)
            {
                var outerElem = polyElem.Elements().FirstOrDefault(e => e.Name.LocalName == "outerBoundaryIs");
                var outerCoords = ParseCoordinates(outerElem?.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates")?.Value);
                if (outerCoords.Length < 3) return null;

                var shell = CreateClosedRing(outerCoords, factory);

                var holes = new List<LinearRing>();
                var inners = polyElem.Elements().Where(e => e.Name.LocalName == "innerBoundaryIs");
                foreach (var inner in inners)
                {
                    var innerCoords = ParseCoordinates(inner.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates")?.Value);
                    if (innerCoords.Length >= 3)
                    {
                        holes.Add(CreateClosedRing(innerCoords, factory));
                    }
                }

                return factory.CreatePolygon(shell, holes.ToArray());
            }

            return null;
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
    }
}