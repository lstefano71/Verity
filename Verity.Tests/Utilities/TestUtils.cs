using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Verity.Tests
{
    public static class TestUtils
    {
        /// <summary>
        /// Asserts that two string collections are equal, ignoring order.
        /// </summary>
        public static void AssertSetEqual(IEnumerable<string> expected, IEnumerable<string> actual)
        {
            Assert.Equal(
                expected.OrderBy(x => x, StringComparer.Ordinal),
                actual.OrderBy(x => x, StringComparer.Ordinal)
            );
        }
    }
}
