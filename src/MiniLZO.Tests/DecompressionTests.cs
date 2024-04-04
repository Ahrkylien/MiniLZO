using System.Diagnostics;
using System.Text;

namespace MiniLZO.Tests
{
    public class DecompressionTests
    {
        private readonly Random _random = new();

        [Fact]
        public void DecompressionTest()
        {
            var compressed = File.ReadAllBytes("Sample Data/compressed log");

            var decompressed = MiniLZO.Decompress(compressed, 31062);

            var decompressedExpected = File.ReadAllBytes("Sample Data/log");

            Assert.Equal<byte>(decompressed, decompressedExpected);
        }
    }
}