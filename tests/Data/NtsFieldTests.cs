using DynamoNTS.Data;
using System;
using Xunit;

namespace DynamoNTS.Tests.Data
{
    public class NtsFieldTests
    {
        [Fact]
        public void InferField_NameWithWhitespace_TrimsName()
        {
            var field = NtsField.InferField("  Caudal  ", Array.Empty<object>());

            Assert.Equal("Caudal", field.Name);
        }
    }
}