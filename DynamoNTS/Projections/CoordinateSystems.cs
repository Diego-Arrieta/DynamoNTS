using Autodesk.DesignScript.Runtime;
using System;
using System.IO;
using ProjCS = ProjNet.CoordinateSystems;

namespace DynamoNTS.Projections
{
    /// <summary>
    /// Represents a Geographic or Projected Coordinate Reference System (CRS).
    /// </summary>
    public class CoordinateSystem
    {
        [IsVisibleInDynamoLibrary(false)]
        internal ProjCS.CoordinateSystem InternalCRS { get; }

        public int EPSG { get; }
        public string Name => InternalCRS?.Name ?? "Unknown CRS";

        [IsVisibleInDynamoLibrary(false)]
        internal CoordinateSystem(ProjCS.CoordinateSystem crs, int epsg = 0)
        {
            InternalCRS = crs ?? throw new ArgumentNullException(nameof(crs));
            EPSG = epsg;
        }

        #region Create Nodes

        public static CoordinateSystem ByEPSG(int epsgCode)
        {
            var csFactory = new ProjCS.CoordinateSystemFactory();

            if (epsgCode == 4326)
            {
                return new CoordinateSystem(ProjCS.GeographicCoordinateSystem.WGS84, 4326);
            }

            if (epsgCode == 3857 || epsgCode == 900913)
            {
                return new CoordinateSystem(ProjCS.ProjectedCoordinateSystem.WebMercator, 3857);
            }

            if (epsgCode >= 32601 && epsgCode <= 32660)
            {
                int zone = epsgCode - 32600;
                string wkt = BuildUtmWkt(zone, isNorth: true);
                var crs = csFactory.CreateFromWkt(wkt);
                return new CoordinateSystem(crs, epsgCode);
            }

            if (epsgCode >= 32701 && epsgCode <= 32760)
            {
                int zone = epsgCode - 32700;
                string wkt = BuildUtmWkt(zone, isNorth: false);
                var crs = csFactory.CreateFromWkt(wkt);
                return new CoordinateSystem(crs, epsgCode);
            }

            throw new NotSupportedException($"EPSG code {epsgCode} is not in the built-in catalog.");
        }

        public static CoordinateSystem ByWKT(string wktString)
        {
            if (string.IsNullOrWhiteSpace(wktString))
                throw new ArgumentException("WKT string cannot be null or empty.", nameof(wktString));

            var csFactory = new ProjCS.CoordinateSystemFactory();
            var crs = csFactory.CreateFromWkt(wktString);

            return new CoordinateSystem(crs);
        }

        public static CoordinateSystem ByPrjFile(string prjFilePath)
        {
            if (string.IsNullOrWhiteSpace(prjFilePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(prjFilePath));

            if (!File.Exists(prjFilePath))
                throw new FileNotFoundException($"Projection file not found: '{prjFilePath}'");

            return ByWKT(File.ReadAllText(prjFilePath));
        }

        public static CoordinateSystem WGS84_UTM(int zone, bool isNorth)
        {
            if (zone < 1 || zone > 60)
                throw new ArgumentOutOfRangeException(nameof(zone), "UTM zone must be between 1 and 60.");

            int epsg = (isNorth ? 32600 : 32700) + zone;
            return ByEPSG(epsg);
        }

        public static CoordinateSystem WGS84 => ByEPSG(4326);
        public static CoordinateSystem WebMercator => ByEPSG(3857);

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