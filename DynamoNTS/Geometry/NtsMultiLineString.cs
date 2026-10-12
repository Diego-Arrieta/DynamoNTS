using Autodesk.DesignScript.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using DynGeometry = Autodesk.DesignScript.Geometry;
using NtsGeometries = NetTopologySuite.Geometries;

namespace DynamoNTS.Geometry
{
    /// <summary>
    /// Represents a collection of topological LineStrings from NetTopologySuite.
    /// </summary>
    public class NtsMultiLineString : NtsGeometry
    {
        [IsVisibleInDynamoLibrary(false)]
        internal NtsGeometries.MultiLineString InternalMultiLineString
        {
            get => (NtsGeometries.MultiLineString)InternalGeometry;
            set => InternalGeometry = value;
        }

        [IsVisibleInDynamoLibrary(false)]
        internal NtsMultiLineString(NtsGeometries.MultiLineString multiLineString) : base(multiLineString)
        {
        }

        #region Create Nodes

        public static NtsMultiLineString ByLineStrings(IEnumerable<NtsLineString> lineStrings)
        {
            if (lineStrings == null) throw new ArgumentNullException(nameof(lineStrings));

            var ntsLines = lineStrings
                .Where(l => l != null && !l.InternalGeometry.IsEmpty)
                .Select(l => l.InternalLineString)
                .ToArray();

            if (ntsLines.Length == 0)
                throw new ArgumentException("At least one valid LineString is required.");

            var factory = NtsGeometries.GeometryFactory.Default;
            return new NtsMultiLineString(factory.CreateMultiLineString(ntsLines));
        }

        #endregion

        #region Action Nodes

        public List<DynGeometry.PolyCurve> ToPolyCurves()
        {
            var list = new List<DynGeometry.PolyCurve>();
            for (int i = 0; i < InternalMultiLineString.NumGeometries; i++)
            {
                if (InternalMultiLineString.GetGeometryN(i) is NtsGeometries.LineString ls)
                {
                    var wrapper = new NtsLineString(ls);
                    list.Add(wrapper.ToPolyCurve());
                }
            }
            return list;
        }

        public List<NtsLineString> Explode()
        {
            var list = new List<NtsLineString>();
            for (int i = 0; i < InternalMultiLineString.NumGeometries; i++)
            {
                if (InternalMultiLineString.GetGeometryN(i) is NtsGeometries.LineString ls)
                {
                    list.Add(new NtsLineString(ls));
                }
            }
            return list;
        }

        #endregion

        #region Query Nodes

        public int Count => InternalMultiLineString.NumGeometries;
        public double Length => InternalMultiLineString.Length;

        #endregion
    }
}
