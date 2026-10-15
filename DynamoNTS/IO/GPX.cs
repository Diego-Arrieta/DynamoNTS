using Autodesk.DesignScript.Runtime;
using DynamoNTS.Data;
using DynamoNTS.Geometry;
using DynamoNTS.Projections;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DynamoNTS.IO
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
        /// Preserves per-vertex timestamps from track and route points into the 'Timestamps' attribute.
        /// </summary>
        /// <param name="filePath">Path to the .gpx file.</param>
        /// <param name="targetCrs">Optional target Coordinate Reference System to reproject the data into.</param>
        /// <returns name="layer">The loaded NtsLayer instance.</returns>
        public static NtsLayer Read(
            string filePath,
            [DefaultArgument("null")] NtsCoordinateSystem? targetCrs = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("GPX file path cannot be null or empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"GPX file not found: '{filePath}'");

            var doc = XDocument.Load(filePath);
            var geomFactory = GeometryFactory.Default;

            var features = new List<NtsFeature>();
            var fieldNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TrackFieldNames(Dictionary<string, object?> attrs)
            {
                foreach (var key in attrs.Keys)
                {
                    if (seenNames.Add(key))
                    {
                        fieldNames.Add(key);
                    }
                }
            }

            var waypoints = doc.Descendants().Where(e => e.Name.LocalName == "wpt");
            foreach (var wpt in waypoints)
            {
                var coord = ParsePointCoordinate(wpt);
                if (coord == null) continue;

                var pointGeom = NtsGeometry.WrapGeometry(geomFactory.CreatePoint(coord));
                if (pointGeom == null) continue;

                var rawAttrs = ExtractStandardAttributes(wpt);
                rawAttrs["Type"] = "Waypoint";

                TrackFieldNames(rawAttrs);
                var record = NtsRecord.FromDictionary(rawAttrs);
                features.Add(NtsFeature.ByRecord(pointGeom, record));
            }

            var routes = doc.Descendants().Where(e => e.Name.LocalName == "rte");
            foreach (var rte in routes)
            {
                var ptElements = rte.Elements().Where(e => e.Name.LocalName == "rtept").ToList();
                var coords = new List<Coordinate>();
                var timestamps = new List<string>();
                DateTime? firstPtTime = null;

                foreach (var ptElem in ptElements)
                {
                    var coord = ParsePointCoordinate(ptElem);
                    if (coord == null) continue;
                    coords.Add(coord);

                    var timeElem = ptElem.Elements().FirstOrDefault(e => e.Name.LocalName == "time");
                    string timeVal = timeElem?.Value ?? string.Empty;
                    timestamps.Add(timeVal);

                    if (!firstPtTime.HasValue && !string.IsNullOrWhiteSpace(timeVal))
                    {
                        if (DateTime.TryParse(timeVal, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
                        {
                            firstPtTime = dt;
                        }
                    }
                }

                if (coords.Count < 2) continue;

                var lineGeom = NtsGeometry.WrapGeometry(geomFactory.CreateLineString(coords.ToArray()));
                if (lineGeom == null) continue;

                var rawAttrs = ExtractStandardAttributes(rte);
                rawAttrs["Type"] = "Route";

                if (timestamps.Any(t => !string.IsNullOrEmpty(t)))
                {
                    rawAttrs["Timestamps"] = string.Join(";", timestamps);
                }

                if (!rawAttrs.ContainsKey("Time") && firstPtTime.HasValue)
                {
                    rawAttrs["Time"] = firstPtTime.Value;
                }

                TrackFieldNames(rawAttrs);
                var record = NtsRecord.FromDictionary(rawAttrs);
                features.Add(NtsFeature.ByRecord(lineGeom, record));
            }

            var tracks = doc.Descendants().Where(e => e.Name.LocalName == "trk");
            foreach (var trk in tracks)
            {
                var trackAttrs = ExtractStandardAttributes(trk);
                trackAttrs["Type"] = "Track";

                var segments = trk.Elements().Where(e => e.Name.LocalName == "trkseg");
                foreach (var seg in segments)
                {
                    var ptElements = seg.Elements().Where(e => e.Name.LocalName == "trkpt").ToList();
                    var coords = new List<Coordinate>();
                    var timestamps = new List<string>();
                    DateTime? firstPtTime = null;

                    foreach (var ptElem in ptElements)
                    {
                        var coord = ParsePointCoordinate(ptElem);
                        if (coord == null) continue;
                        coords.Add(coord);

                        var timeElem = ptElem.Elements().FirstOrDefault(e => e.Name.LocalName == "time");
                        string timeVal = timeElem?.Value ?? string.Empty;
                        timestamps.Add(timeVal);

                        if (!firstPtTime.HasValue && !string.IsNullOrWhiteSpace(timeVal))
                        {
                            if (DateTime.TryParse(timeVal, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
                            {
                                firstPtTime = dt;
                            }
                        }
                    }

                    if (coords.Count < 2) continue;

                    var lineGeom = NtsGeometry.WrapGeometry(geomFactory.CreateLineString(coords.ToArray()));
                    if (lineGeom == null) continue;

                    var rawAttrs = new Dictionary<string, object?>(trackAttrs, StringComparer.OrdinalIgnoreCase);

                    if (timestamps.Any(t => !string.IsNullOrEmpty(t)))
                    {
                        rawAttrs["Timestamps"] = string.Join(";", timestamps);
                    }

                    if (!rawAttrs.ContainsKey("Time") && firstPtTime.HasValue)
                    {
                        rawAttrs["Time"] = firstPtTime.Value;
                    }

                    TrackFieldNames(rawAttrs);
                    var record = NtsRecord.FromDictionary(rawAttrs);
                    features.Add(NtsFeature.ByRecord(lineGeom, record));
                }
            }

            if (features.Count == 0)
                throw new InvalidOperationException("No valid waypoints, routes, or tracks could be parsed from the GPX file.");

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
        /// Writes an NtsLayer to disk as a GPX 1.1 file.
        /// Reprojects to WGS84 if a different CRS is defined, or validates geographic coordinate bounds if CRS is undefined.
        /// Adheres strictly to GPX 1.1 schema sequence and warns if polygon interior holes are omitted.
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
                ValidateGeographicBounds(layer, "GPX");
            }

            var metadataElem = new XElement(GpxNs + "metadata",
                new XElement(GpxNs + "name", exportLayer.Name));

            var env = exportLayer.Extents()?.InternalGeometry?.EnvelopeInternal;
            if (env != null && !env.IsNull)
            {
                metadataElem.Add(new XElement(GpxNs + "bounds",
                    new XAttribute("minlat", env.MinY.ToString("F8", CultureInfo.InvariantCulture)),
                    new XAttribute("minlon", env.MinX.ToString("F8", CultureInfo.InvariantCulture)),
                    new XAttribute("maxlat", env.MaxY.ToString("F8", CultureInfo.InvariantCulture)),
                    new XAttribute("maxlon", env.MaxX.ToString("F8", CultureInfo.InvariantCulture))));
            }

            var gpxRoot = new XElement(GpxNs + "gpx",
                new XAttribute("version", "1.1"),
                new XAttribute("creator", "DynamoNTS"),
                metadataElem);

            foreach (var feature in exportLayer.Features)
            {
                if (feature?.Geometry == null) continue;


                if (feature.Geometry is NtsPoint pt)
                {
                    AddWaypoint(gpxRoot, pt.InternalPoint.Coordinate, feature);
                }
                else if (feature.Geometry is NtsMultiPoint mp)
                {
                    foreach (var p in mp.Explode())
                    {
                        AddWaypoint(gpxRoot, p.InternalPoint.Coordinate, feature);
                    }
                }

                else if (feature.Geometry is NtsLineString line)
                {
                    var trkElem = new XElement(GpxNs + "trk");
                    AppendTrackMetadata(trkElem, feature);
                    trkElem.Add(CreateTrackSegment(line.InternalLineString.Coordinates));
                    gpxRoot.Add(trkElem);
                }
                else if (feature.Geometry is NtsMultiLineString mline)
                {
                    var trkElem = new XElement(GpxNs + "trk");
                    AppendTrackMetadata(trkElem, feature);
                    foreach (var subLine in mline.Explode())
                    {
                        trkElem.Add(CreateTrackSegment(subLine.InternalLineString.Coordinates));
                    }
                    gpxRoot.Add(trkElem);
                }

                else if (feature.Geometry is NtsPolygon poly)
                {
                    if (poly.InternalPolygon.NumInteriorRings > 0)
                    {
                        Trace.TraceWarning(
                            $"[GPX.Write] Warning: Polygon in layer '{exportLayer.Name}' contains " +
                            $"{poly.InternalPolygon.NumInteriorRings} interior ring(s) (holes). GPX only supports linear tracks and cannot represent holes; interior rings were omitted.");
                    }

                    var trkElem = new XElement(GpxNs + "trk");
                    AppendTrackMetadata(trkElem, feature);
                    trkElem.Add(CreateTrackSegment(poly.InternalPolygon.ExteriorRing.Coordinates));
                    gpxRoot.Add(trkElem);
                }
                else if (feature.Geometry is NtsMultiPolygon mpoly)
                {
                    int totalHoles = mpoly.InternalMultiPolygon.Geometries
                        .OfType<Polygon>()
                        .Sum(p => p.NumInteriorRings);

                    if (totalHoles > 0)
                    {
                        Trace.TraceWarning(
                            $"[GPX.Write] Warning: MultiPolygon in layer '{exportLayer.Name}' contains " +
                            $"{totalHoles} total interior ring(s) (holes). GPX only supports linear tracks and cannot represent holes; interior rings were omitted.");
                    }

                    var trkElem = new XElement(GpxNs + "trk");
                    AppendTrackMetadata(trkElem, feature);
                    foreach (var subPoly in mpoly.Explode())
                    {
                        trkElem.Add(CreateTrackSegment(subPoly.InternalPolygon.ExteriorRing.Coordinates));
                    }
                    gpxRoot.Add(trkElem);
                }
            }

            var xDoc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), gpxRoot);
            xDoc.Save(filePath);

            return true;
        }

        #endregion

        #region Internal Helpers

        /// <summary>
        /// Adds a waypoint adhering to GPX 1.1 child order: ele -> time -> name -> desc.
        /// </summary>
        private static void AddWaypoint(XElement root, Coordinate coord, NtsFeature feature)
        {
            if (coord == null) return;

            var wptElem = new XElement(GpxNs + "wpt",
                new XAttribute("lat", coord.Y.ToString("F8", CultureInfo.InvariantCulture)),
                new XAttribute("lon", coord.X.ToString("F8", CultureInfo.InvariantCulture)));

            if (!double.IsNaN(coord.Z))
            {
                wptElem.Add(new XElement(GpxNs + "ele", coord.Z.ToString("F3", CultureInfo.InvariantCulture)));
            }

            object? time = feature.GetValue("Time") ?? feature.GetValue("time");
            if (time is DateTime dt)
            {
                wptElem.Add(new XElement(GpxNs + "time", dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
            }
            else if (time is string timeStr && !string.IsNullOrWhiteSpace(timeStr))
            {
                if (DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedDt))
                {
                    wptElem.Add(new XElement(GpxNs + "time", parsedDt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")));
                }
            }

            object? name = feature.GetValue("Name") ?? feature.GetValue("name");
            if (name != null && !string.IsNullOrWhiteSpace(name.ToString()))
            {
                wptElem.Add(new XElement(GpxNs + "name", name.ToString()));
            }

            // 4. <desc>
            object? desc = feature.GetValue("Description") ?? feature.GetValue("desc") ?? feature.GetValue("description");
            if (desc != null && !string.IsNullOrWhiteSpace(desc.ToString()))
            {
                wptElem.Add(new XElement(GpxNs + "desc", desc.ToString()));
            }

            root.Add(wptElem);
        }

        /// <summary>
        /// Appends track metadata allowed by GPX 1.1 trkType (name and desc).
        /// Note: GPX 1.1 trkType does not allow a direct <time> child element.
        /// </summary>
        private static void AppendTrackMetadata(XElement trkElem, NtsFeature feature)
        {
            object? name = feature.GetValue("Name") ?? feature.GetValue("name");
            if (name != null && !string.IsNullOrWhiteSpace(name.ToString()))
            {
                trkElem.Add(new XElement(GpxNs + "name", name.ToString()));
            }

            object? desc = feature.GetValue("Description") ?? feature.GetValue("desc") ?? feature.GetValue("description");
            if (desc != null && !string.IsNullOrWhiteSpace(desc.ToString()))
            {
                trkElem.Add(new XElement(GpxNs + "desc", desc.ToString()));
            }
        }

        private static XElement CreateTrackSegment(Coordinate[] coordinates)
        {
            var trkSeg = new XElement(GpxNs + "trkseg");
            foreach (var coord in coordinates)
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
            return trkSeg;
        }

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