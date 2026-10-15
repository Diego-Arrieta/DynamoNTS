using Autodesk.DesignScript.Runtime;
using DynamoNTS.Geometry;
using NetTopologySuite.IO;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using NtsGeom = NetTopologySuite.Geometries.Geometry;

namespace DynamoNTS.IO
{
    /// <summary>
    /// Provides methods to import and export geometries using OGC Well-Known Binary (WKB) and PostGIS Extended WKB (EWKB).
    /// </summary>
    public static class WKB
    {
        #region Action Nodes

        /// <summary>
        /// Reconstructs an NtsGeometry from a Well-Known Binary (WKB) or Extended WKB (EWKB) hexadecimal string.
        /// Supports '0x' or '\x' prefixes, PostGIS 'SRID=...;' headers, and embedded EWKB SRID definitions.
        /// </summary>
        /// <param name="hexString">The hexadecimal WKB or EWKB string.</param>
        /// <returns name="geometry">The imported NtsGeometry, or null if parsing fails.</returns>
        public static NtsGeometry? FromHex(string hexString)
        {
            if (string.IsNullOrWhiteSpace(hexString))
                throw new ArgumentException("Hexadecimal WKB string cannot be null or empty.", nameof(hexString));

            var (bytes, prefixSrid) = CleanAndParseHex(hexString);

            var reader = new WKBReader
            {
                RepairRings = true
            };

            var geom = reader.Read(bytes);
            if (geom == null) return null;

            if (prefixSrid.HasValue && prefixSrid.Value > 0)
            {
                geom.SRID = prefixSrid.Value;
            }

            return NtsGeometry.WrapGeometry(geom);
        }

        /// <summary>
        /// Reconstructs an NtsGeometry directly from raw Well-Known Binary (WKB) bytes.
        /// </summary>
        /// <param name="bytes">Raw byte array representing WKB or EWKB.</param>
        /// <returns name="geometry">The imported NtsGeometry, or null if parsing fails.</returns>
        public static NtsGeometry? FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("WKB byte array cannot be null or empty.", nameof(bytes));

            var reader = new WKBReader
            {
                RepairRings = true
            };

            var geom = reader.Read(bytes);
            return NtsGeometry.WrapGeometry(geom);
        }

        /// <summary>
        /// Converts an NtsGeometry into its Well-Known Binary (WKB) hexadecimal string representation.
        /// Includes EWKB SRID metadata if the geometry has an assigned SRID.
        /// </summary>
        /// <param name="geometry">The topological geometry to serialize.</param>
        /// <returns name="hexString">Hexadecimal WKB string.</returns>
        public static string ToHex(NtsGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            byte[] bytes = ToBytes(geometry);
            return BytesToHex(bytes);
        }

        /// <summary>
        /// Converts an NtsGeometry into raw Well-Known Binary (WKB/EWKB) bytes.
        /// </summary>
        /// <param name="geometry">The topological geometry to serialize.</param>
        /// <returns name="bytes">Raw WKB byte array.</returns>
        public static byte[] ToBytes(NtsGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            bool includeSrid = geometry.InternalGeometry.SRID > 0;
            var writer = new WKBWriter(ByteOrder.LittleEndian, includeSrid);

            return writer.Write(geometry.InternalGeometry);
        }

        #endregion

        #region Internal Helpers (B37)

        [IsVisibleInDynamoLibrary(false)]
        internal static (byte[] bytes, int? srid) CleanAndParseHex(string input)
        {
            string clean = input.Trim();
            int? extractedSrid = null;

            if (clean.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase))
            {
                int semicolonIdx = clean.IndexOf(';');
                if (semicolonIdx > 0)
                {
                    string sridPart = clean.Substring(0, semicolonIdx);
                    clean = clean.Substring(semicolonIdx + 1).Trim();

                    var match = Regex.Match(sridPart, @"SRID\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int srid))
                    {
                        extractedSrid = srid;
                    }
                }
            }

            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("\\x", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(2);
            }

            clean = clean.Replace(" ", string.Empty).Replace("-", string.Empty);

            if (clean.Length == 0)
                throw new ArgumentException("The hexadecimal string is empty after removing formatting prefixes.", nameof(input));

            if (clean.Length % 2 != 0)
            {
                throw new ArgumentException(
                    $"Invalid hexadecimal WKB string: length is {clean.Length}. Hex strings must have an even number of characters (2 characters per byte).",
                    nameof(input));
            }

            byte[] bytes = new byte[clean.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                string byteHex = clean.Substring(i * 2, 2);
                if (!byte.TryParse(byteHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                {
                    throw new FormatException($"Invalid hexadecimal character sequence '{byteHex}' encountered at position {i * 2}.");
                }
                bytes[i] = b;
            }

            return (bytes, extractedSrid);
        }

        private static string BytesToHex(byte[] bytes)
        {
            char[] c = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int b = bytes[i] >> 4;
                c[i * 2] = (char)(55 + b + (((b - 10) >> 31) & -7));
                b = bytes[i] & 0xF;
                c[i * 2 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
            }
            return new string(c);
        }

        #endregion
    }
}