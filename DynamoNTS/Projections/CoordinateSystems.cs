using Autodesk.DesignScript.Runtime;
using System;
using System.IO;
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
        /// Supports WGS84 (4326), Web Mercator (3857), and standard WGS84 UTM zones (32601-32660, 32701-32760).
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
        /// </summary>
        /// <param name="wktString">The Well-Known Text (WKT) coordinate system definition string.</param>
        /// <returns name="coordinateSystem">The instantiated NtsCoordinateSystem instance.</returns>
        public static NtsCoordinateSystem ByWKT(string wktString)
        {
            if (string.IsNullOrWhiteSpace(wktString))
                throw new ArgumentException("WKT string cannot be null or empty.", nameof(wktString));

            var csFactory = new ProjCS.CoordinateSystemFactory();
            var crs = csFactory.CreateFromWkt(wktString);

            return new NtsCoordinateSystem(crs);
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

        #region Helper Methods

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