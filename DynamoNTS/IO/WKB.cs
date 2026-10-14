using Autodesk.DesignScript.Runtime;
using DynamoNTS.Geometry;
using NetTopologySuite.IO;
using System;
using System.Text;

namespace DynamoNTS.IO
{
    /// <summary>
    /// Provides methods to serialize and deserialize geometries using the OGC Well-Known Binary (WKB) standard.
    /// </summary>
    public static class WKB
    {
        #region Hexadecimal Action Nodes

        /// <summary>
        /// Creates an NtsGeometry from a Well-Known Binary hexadecimal string.
        /// </summary>
        /// <param name="hexString">The WKB hexadecimal string.</param>
        /// <returns name="geometry">The imported topological geometry.</returns>
        public static NtsGeometry FromHex(string hexString)
        {
            if (string.IsNullOrWhiteSpace(hexString))
                throw new ArgumentException("WKB hex string cannot be null or empty.", nameof(hexString));

            var bytes = HexToBytes(hexString.Trim());
            return FromBytes(bytes);
        }

        /// <summary>
        /// Serializes an NtsGeometry into a Well-Known Binary hexadecimal string.
        /// </summary>
        /// <param name="geometry">The topological geometry to serialize.</param>
        /// <returns name="hexString">The hexadecimal representation of the geometry.</returns>
        public static string ToHex(NtsGeometry geometry)
        {
            if (geometry is null)
                throw new ArgumentNullException(nameof(geometry));

            var bytes = ToBytes(geometry);
            return BytesToHex(bytes);
        }

        #endregion

        #region Binary Action Nodes

        /// <summary>
        /// Creates an NtsGeometry from raw WKB binary bytes.
        /// </summary>
        /// <param name="bytes">Raw byte array.</param>
        /// <returns name="geometry">The imported topological geometry.</returns>
        public static NtsGeometry FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("Byte array cannot be null or empty.", nameof(bytes));

            var reader = new WKBReader();
            var ntsGeom = reader.Read(bytes);

            return NtsGeometry.WrapGeometry(ntsGeom)
                   ?? throw new InvalidOperationException("Failed to construct a valid geometry from WKB binary data.");
        }

        /// <summary>
        /// Converts an NtsGeometry into raw WKB binary bytes.
        /// </summary>
        /// <param name="geometry">The topological geometry to serialize.</param>
        /// <returns name="bytes">Raw byte array representation.</returns>
        public static byte[] ToBytes(NtsGeometry geometry)
        {
            if (geometry is null)
                throw new ArgumentNullException(nameof(geometry));

            var writer = new WKBWriter();
            return writer.Write(geometry.InternalGeometry);
        }

        #endregion

        #region Internal Helpers

        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return [];

            int numberChars = hex.Length;
            byte[] bytes = new byte[numberChars / 2];
            for (int i = 0; i < numberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        #endregion
    }
}