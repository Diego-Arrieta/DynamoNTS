using Autodesk.DesignScript.Runtime;
using System;
using System.IO;
using System.Text.RegularExpressions;
using ProjCS = ProjNet.CoordinateSystems;

namespace DynamoNTS.Projections
{
    /// <summary>
    /// Represents a Geographic or Projected Coordinate Reference System (CRS).
    /// </summary>
    public class NtsCoordinateSystem
    {
        [IsVisibleInDynamoLibrary(false)]
        internal ProjCS.CoordinateSystem InternalCRS { get; }

        /// <summary>
        /// Gets the European Petroleum Survey Group (EPSG) numeric identifier code, or 0 if undefined.
        /// </summary>
        public int EPSG { get; }

        /// <summary>
        /// Gets the descriptive name of the Coordinate Reference System.
        /// </summary>
        public string Name => InternalCRS?.Name ?? "Unknown CRS";

        [IsVisibleInDynamoLibrary(false)]
        internal NtsCoordinateSystem(ProjCS.CoordinateSystem crs, int epsg = 0)
        {
            InternalCRS = crs ?? throw new ArgumentNullException(nameof(crs));
            EPSG = epsg;
        }

        #region Create Nodes

        /// <summary>
        /// Resolves a Coordinate Reference System using its standardized EPSG authority code.
        /// Supports WGS84 (4326), Web Mercator (3857, 900913), and standard WGS84 UTM zones (32601-32660, 32701-32760).
        /// </summary>
        /// <param name="epsgCode">The numeric EPSG identifier code.</param>
        /// <returns name="coordinateSystem">The resolved NtsCoordinateSystem instance.</returns>
        public static NtsCoordinateSystem ByEPSG(int epsgCode)
        {
            var csFactory = new ProjCS.CoordinateSystemFactory();

            if (epsgCode == 4326)
            {
                return new NtsCoordinateSystem(ProjCS.GeographicCoordinateSystem.WGS84, 4326);
            }

            if (epsgCode == 3857 || epsgCode == 900913)
            {
                return new NtsCoordinateSystem(ProjCS.ProjectedCoordinateSystem.WebMercator, 3857);
            }

            if (epsgCode >= 32601 && epsgCode <= 32660)
            {
                int zone = epsgCode - 32600;
                string wkt = BuildUtmWkt(zone, isNorth: true);
                var crs = csFactory.CreateFromWkt(wkt);
                return new NtsCoordinateSystem(crs, epsgCode);
            }

            if (epsgCode >= 32701 && epsgCode <= 32760)
            {
                int zone = epsgCode - 32700;
                string wkt = BuildUtmWkt(zone, isNorth: false);
                var crs = csFactory.CreateFromWkt(wkt);
                return new NtsCoordinateSystem(crs, epsgCode);
            }

            throw new NotSupportedException($"EPSG code {epsgCode} is not in the built-in catalog.");
        }

        /// <summary>
        /// Creates a Coordinate Reference System from an OGC Well-Known Text (WKT) projection string.
        /// Automatically extracts the EPSG authority code if defined.
        /// </summary>
        /// <param name="wktString">The Well-Known Text (WKT) coordinate system definition string.</param>
        /// <returns name="coordinateSystem">The instantiated NtsCoordinateSystem instance.</returns>
        public static NtsCoordinateSystem ByWKT(string wktString)
        {
            if (string.IsNullOrWhiteSpace(wktString))
                throw new ArgumentException("WKT string cannot be null or empty.", nameof(wktString));

            var csFactory = new ProjCS.CoordinateSystemFactory();
            var crs = csFactory.CreateFromWkt(wktString);

            int epsg = ExtractEpsgCode(crs, wktString);
            return new NtsCoordinateSystem(crs, epsg);
        }

        /// <summary>
        /// Reads and initializes a Coordinate Reference System from an ESRI projection file (.prj).
        /// </summary>
        /// <param name="prjFilePath">Absolute file path to the .prj file.</param>
        /// <returns name="coordinateSystem">The instantiated NtsCoordinateSystem instance.</returns>
        public static NtsCoordinateSystem ByPrjFile(string prjFilePath)
        {
            if (string.IsNullOrWhiteSpace(prjFilePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(prjFilePath));

            if (!File.Exists(prjFilePath))
                throw new FileNotFoundException($"Projection file not found: '{prjFilePath}'");

            return ByWKT(File.ReadAllText(prjFilePath));
        }

        /// <summary>
        /// Creates a WGS 84 Universal Transverse Mercator (UTM) projected coordinate system by zone and hemisphere.
        /// </summary>
        /// <param name="zone">UTM zone number (1 to 60).</param>
        /// <param name="isNorth">True for Northern hemisphere (N), false for Southern hemisphere (S).</param>
        /// <returns name="coordinateSystem">The resulting UTM projected Coordinate Reference System.</returns>
        public static NtsCoordinateSystem WGS84_UTM(int zone, bool isNorth)
        {
            if (zone < 1 || zone > 60)
                throw new ArgumentOutOfRangeException(nameof(zone), "UTM zone must be between 1 and 60.");

            int epsg = (isNorth ? 32600 : 32700) + zone;
            return ByEPSG(epsg);
        }

        /// <summary>
        /// Gets the standard WGS 84 Geographic Coordinate System (EPSG:4326).
        /// </summary>
        public static NtsCoordinateSystem WGS84 => ByEPSG(4326);

        /// <summary>
        /// Gets the standard WGS 84 / Pseudo-Mercator projection (EPSG:3857).
        /// </summary>
        public static NtsCoordinateSystem WebMercator => ByEPSG(3857);

        #endregion

        #region Comparison & Equivalence

        /// <summary>
        /// Determines whether this coordinate reference system is functionally equivalent to another CRS
        /// by matching EPSG identifiers, ProjNet parameters, or WKT definitions.
        /// </summary>
        /// <param name="other">The coordinate reference system to compare against.</param>
        /// <returns>True if both systems represent the same coordinate definition; otherwise, false.</returns>
        public bool IsEquivalentTo(NtsCoordinateSystem? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            if (EPSG != 0 && other.EPSG != 0 && EPSG == other.EPSG)
                return true;

            if (InternalCRS != null && other.InternalCRS != null)
            {
                try
                {
                    if (InternalCRS.EqualParams(other.InternalCRS))
                        return true;
                }
                catch
                {
                    // Fall back to WKT comparison if parameter extraction fails
                }

                if (string.Equals(InternalCRS.WKT, other.InternalCRS.WKT, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #endregion

        #region Internal Helpers

        private static int ExtractEpsgCode(ProjCS.CoordinateSystem crs, string wktString)
        {
            if (!string.IsNullOrEmpty(crs.Authority) && crs.Authority.Equals("EPSG", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(crs.AuthorityCode.ToString(), out int parsedEpsg) && parsedEpsg > 0)
                {
                    return parsedEpsg;
                }
            }

            var match = Regex.Match(
                wktString,
                @"AUTHORITY\[\s*""EPSG""\s*,\s*""?(\d+)""?\s*\]",
                RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

            if (match.Success && int.TryParse(match.Groups[1].Value, out int regexEpsg) && regexEpsg > 0)
            {
                return regexEpsg;
            }

            return 0;
        }

        private static string BuildUtmWkt(int zone, bool isNorth)
        {
            int centralMeridian = (zone * 6) - 183;
            double falseNorthing = isNorth ? 0.0 : 10000000.0;
            string hemi = isNorth ? "N" : "S";
            int epsg = (isNorth ? 32600 : 32700) + zone;

            return $@"PROJCS[""WGS 84 / UTM zone {zone}{hemi}"",
                GEOGCS[""WGS 84"",
                    DATUM[""WGS_1984"",
                        SPHEROID[""WGS 84"",6378137,298.257223563,AUTHORITY[""EPSG"",""7030""]],
                        AUTHORITY[""EPSG"",""6326""]],
                    PRIMEM[""Greenwich"",0,AUTHORITY[""EPSG"",""8901""]],
                    UNIT[""degree"",0.0174532925199433,AUTHORITY[""EPSG"",""9122""]],
                    AUTHORITY[""EPSG"",""4326""]],
                PROJECTION[""Transverse_Mercator""],
                PARAMETER[""latitude_of_origin"",0],
                PARAMETER[""central_meridian"",{centralMeridian}],
                PARAMETER[""scale_factor"",0.9996],
                PARAMETER[""false_easting"",500000],
                PARAMETER[""false_northing"",{falseNorthing}],
                UNIT[""metre"",1,AUTHORITY[""EPSG"",""9001""]],
                AUTHORITY[""EPSG"",""{epsg}""]]";
        }

        #endregion
    }
}