using Autodesk.DesignScript.Runtime;
using DynamoNTS.Features;
using DynamoNTS.Fields;
using DynamoNTS.Geometry;
using DynamoNTS.Layers;
using DynamoNTS.Projections;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DynamoNTS.ImportExport
{
    /// <summary>
    /// Provides methods to import and export GPS Exchange Format (.gpx) vector datasets.
    /// GPX operates natively in WGS84 geographic coordinates (EPSG:4326).
    /// </summary>
    public static class GPX
    {
        private static readonly XNamespace GpxNs = "http://www.topografix.com/GPX/1/1";

        #region Action Nodes

        /// <summary>
        /// Reads a GPX file from disk and reconstructs Waypoints, Routes, and Tracks into an NtsLayer.
        /// </summary>
        /// <param name="filePath">Path to the .gpx file.</param>
        /// <param name="targetCrs">Optional target Coordinate Reference System to reproject the data into.</param>
        /// <returns name="layer">The loaded NtsLayer instance.</returns>
        public static NtsLayer Read(
            string filePath,
            [DefaultArgument("null")] CoordinateSystem? targetCrs = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("GPX file path cannot be null or empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"GPX file not found: '{filePath}'");

            var doc = XDocument.Load(filePath);
            var geomFactory = GeometryFactory.Default;

            var features = new List<NtsFeature>();
            var fieldsMap = new Dictionary<string, NtsField>(StringComparer.OrdinalIgnoreCase);

            // 1. Parse Waypoints (<wpt>) -> NtsPoint
            var waypoints = doc.Descendants().Where(e => e.Name.LocalName == "wpt");
            foreach (var wpt in waypoints)
            {
                var coord = ParsePointCoordinate(wpt);
                if (coord == null) continue;

                var pointGeom = NtsGeometry.WrapGeometry(geomFactory.CreatePoint(coord));
                if (pointGeom == null) continue;

                var rawAttrs = ExtractStandardAttributes(wpt);
                rawAttrs["Type"] = "Waypoint";

                RegisterFields(rawAttrs, fieldsMap);
                var record = NtsRecord.FromDictionary(rawAttrs);
                features.Add(NtsFeature.ByRecord(pointGeom, record));
            }

            // 2. Parse Routes (<rte>) -> NtsLineString
            var routes = doc.Descendants().Where(e => e.Name.LocalName == "rte");
            foreach (var rte in routes)
            {
                var coords = rte.Elements()
                    .Where(e => e.Name.LocalName == "rtept")
                    .Select(ParsePointCoordinate)
                    .Where(c => c != null)
                    .Select(c => c!)
                    .ToArray();

                if (coords.Length < 2) continue;

                var lineGeom = NtsGeometry.WrapGeometry(geomFactory.CreateLineString(coords));
                if (lineGeom == null) continue;

                var rawAttrs = ExtractStandardAttributes(rte);
                rawAttrs["Type"] = "Route";

                RegisterFields(rawAttrs, fieldsMap);
                var record = NtsRecord.FromDictionary(rawAttrs);
                features.Add(NtsFeature.ByRecord(lineGeom, record));
            }

            // 3. Parse Tracks (<trk>) -> NtsLineString (per track segment)
            var tracks = doc.Descendants().Where(e => e.Name.LocalName == "trk");
            foreach (var trk in tracks)
            {
                var trackAttrs = ExtractStandardAttributes(trk);
                trackAttrs["Type"] = "Track";

                var segments = trk.Elements().Where(e => e.Name.LocalName == "trkseg");
                foreach (var seg in segments)
                {
                    var coords = seg.Elements()
                        .Where(e => e.Name.LocalName == "trkpt")
                        .Select(ParsePointCoordinate)
                        .Where(c => c != null)
                        .Select(c => c!)
                        .ToArray();

                    if (coords.Length < 2) continue;

                    var lineGeom = NtsGeometry.WrapGeometry(geomFactory.CreateLineString(coords));
                    if (lineGeom == null) continue;

                    var rawAttrs = new Dictionary<string, object?>(trackAttrs, StringComparer.OrdinalIgnoreCase);
                    RegisterFields(rawAttrs, fieldsMap);

                    var record = NtsRecord.FromDictionary(rawAttrs);
                    features.Add(NtsFeature.ByRecord(lineGeom, record));
                }
            }

            if (features.Count == 0)
                throw new InvalidOperationException("No valid waypoints, routes, or tracks could be parsed from the GPX file.");

            var schema = fieldsMap.Count > 0
                ? NtsSchema.ByFields(fieldsMap.Values.ToList())
                : NtsSchema.Empty();

            string layerName = Path.GetFileNameWithoutExtension(filePath);
            var layer = NtsLayer.ByFeatures(features, layerName, CoordinateSystem.WGS84, schema);

            // Reproject if a destination CRS was requested
            if (targetCrs != null && targetCrs.EPSG != CoordinateSystem.WGS84.EPSG)
            {
                return layer.Reproject(targetCrs);
            }

            return layer;
        }

        /// <summary>
        /// Writes an NtsLayer to disk as a GPX 1.1 file. Reprojects to WGS84 if a different CRS is defined.
        /// </summary>
        /// <param name="layer">The NtsLayer to export.</param>
        /// <param name="filePath">Target destination path (.gpx).</param>
        /// <returns name="success">True if export succeeded.</returns>
        public static bool Write(NtsLayer layer, string filePath)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Destination path cannot be null or empty.", nameof(filePath));

            if (layer.Count == 0)
                throw new InvalidOperationException("Cannot write an empty layer to GPX.");

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // GPX requires WGS84 (EPSG:4326)
            NtsLayer exportLayer = layer;
            if (layer.CoordinateSystem != null && layer.CoordinateSystem.EPSG != 4326)
            {
                exportLayer = layer.Reproject(CoordinateSystem.WGS84);
            }

            var gpxRoot = new XElement(GpxNs + "gpx",
                new XAttribute("version", "1.1"),
                new XAttribute("creator", "DynamoNTS"),
                new XElement(GpxNs + "metadata",
                    new XElement(GpxNs + "name", exportLayer.Name)));

            foreach (var feature in exportLayer.Features)
            {
                if (feature?.Geometry == null) continue;

                // Points -> <wpt>
                if (feature.Geometry is NtsPoint pt)
                {
                    var coord = pt.InternalGeometry.Coordinate;
                    if (coord == null) continue;

                    var wptElem = new XElement(GpxNs + "wpt",
                        new XAttribute("lat", coord.Y.ToString("F8", CultureInfo.InvariantCulture)),
                        new XAttribute("lon", coord.X.ToString("F8", CultureInfo.InvariantCulture)));

                    if (!double.IsNaN(coord.Z))
                    {
                        wptElem.Add(new XElement(GpxNs + "ele", coord.Z.ToString("F3", CultureInfo.InvariantCulture)));
                    }

                    AppendFeatureMetadata(wptElem, feature);
                    gpxRoot.Add(wptElem);
                }
                // LineStrings and Polygons -> <trk>
                else if (feature.Geometry is NtsLineString line)
                {
                    var trkElem = new XElement(GpxNs + "trk");
                    AppendFeatureMetadata(trkElem, feature);

                    var trkSeg = new XElement(GpxNs + "trkseg");
                    foreach (var coord in line.InternalGeometry.Coordinates)
                    {
                        var ptElem = new XElement(GpxNs + "trkpt",
                            new XAttribute("lat", coord.Y.ToString("F8", CultureInfo.InvariantCulture)),
                            new XAttribute("lon", coord.X.ToString("F8", CultureInfo.InvariantCulture)));

                        if (!double.IsNaN(coord.Z))
                        {
                            ptElem.Add(new XElement(GpxNs + "ele", coord.Z.ToString("F3", CultureInfo.InvariantCulture)));
                        }

                        trkSeg.Add(ptElem);
                    }

                    trkElem.Add(trkSeg);
                    gpxRoot.Add(trkElem);
                }
                else if (feature.Geometry is NtsPolygon poly)
                {
                    var trkElem = new XElement(GpxNs + "trk");
                    AppendFeatureMetadata(trkElem, feature);

                    var trkSeg = new XElement(GpxNs + "trkseg");
                    foreach (var coord in poly.InternalPolygon.ExteriorRing.Coordinates)
                    {
                        var ptElem = new XElement(GpxNs + "trkpt",
                            new XAttribute("lat", coord.Y.ToString("F8", CultureInfo.InvariantCulture)),
                            new XAttribute("lon", coord.X.ToString("F8", CultureInfo.InvariantCulture)));

                        if (!double.IsNaN(coord.Z))
                        {
                            ptElem.Add(new XElement(GpxNs + "ele", coord.Z.ToString("F3", CultureInfo.InvariantCulture)));
                        }

                        trkSeg.Add(ptElem);
                    }

                    trkElem.Add(trkSeg);
                    gpxRoot.Add(trkElem);
                }
            }

            var xDoc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), gpxRoot);
            xDoc.Save(filePath);

            return true;
        }

        #endregion

        #region Internal Helpers

        private static Coordinate? ParsePointCoordinate(XElement elem)
        {
            var latAttr = elem.Attribute("lat")?.Value;
            var lonAttr = elem.Attribute("lon")?.Value;

            if (latAttr == null || lonAttr == null) return null;

            if (double.TryParse(latAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                double.TryParse(lonAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            {
                var eleElem = elem.Elements().FirstOrDefault(e => e.Name.LocalName == "ele");
                if (eleElem != null && double.TryParse(eleElem.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ele))
                {
                    return new CoordinateZ(lon, lat, ele);
                }

                return new Coordinate(lon, lat);
            }

            return null;
        }

        private static Dictionary<string, object?> ExtractStandardAttributes(XElement element)
        {
            var attrs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            var nameElem = element.Elements().FirstOrDefault(e => e.Name.LocalName == "name");
            if (nameElem != null && !string.IsNullOrWhiteSpace(nameElem.Value))
            {
                attrs["Name"] = nameElem.Value;
            }

            var descElem = element.Elements().FirstOrDefault(e => e.Name.LocalName == "desc");
            if (descElem != null && !string.IsNullOrWhiteSpace(descElem.Value))
            {
                attrs["Description"] = descElem.Value;
            }

            var timeElem = element.Elements().FirstOrDefault(e => e.Name.LocalName == "time");
            if (timeElem != null && !string.IsNullOrWhiteSpace(timeElem.Value))
            {
                if (DateTime.TryParse(timeElem.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    attrs["Time"] = dt;
                }
                else
                {
                    attrs["Time"] = timeElem.Value;
                }
            }

            var eleElem = element.Elements().FirstOrDefault(e => e.Name.LocalName == "ele");
            if (eleElem != null && double.TryParse(eleElem.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ele))
            {
                attrs["Elevation"] = ele;
            }

            return attrs;
        }

        private static void RegisterFields(Dictionary<string, object?> attrs, Dictionary<string, NtsField> fieldsMap)
        {
            foreach (var kvp in attrs)
            {
                if (!fieldsMap.ContainsKey(kvp.Key))
                {
                    fieldsMap[kvp.Key] = NtsField.ByNameAndType(kvp.Key, NtsAttribute.InferType(kvp.Value));
                }
            }
        }

        private static void AppendFeatureMetadata(XElement targetElem, NtsFeature feature)
        {
            object? name = feature.GetValue("Name") ?? feature.GetValue("name");
            if (name != null)
            {
                targetElem.Add(new XElement(GpxNs + "name", name.ToString()));
            }

            object? desc = feature.GetValue("Description") ?? feature.GetValue("desc") ?? feature.GetValue("description");
            if (desc != null)
            {
                targetElem.Add(new XElement(GpxNs + "desc", desc.ToString()));
            }

            object? time = feature.GetValue("Time") ?? feature.GetValue("time");
            if (time is DateTime dt)
            {
                targetElem.Add(new XElement(GpxNs + "time", dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
            }
        }

        #endregion
    }
}
