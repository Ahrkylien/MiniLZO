namespace MiniLZO.Tests
{
    public class CompressionDecompressionTests
    {
        private readonly Random _random = new();

        [Fact]
        public void CompressionDecompressionTestLong()
        {
            CompressionDecompressionTest(2000);
        }

        [Fact]
        public void CompressionDecompressionTestLongRandom()
        {
            CompressionDecompressionTest(2000, randomData: true);
        }

        [Fact]
        public void CompressionDecompressionTestShort()
        {
            List<int> dataLengths = [];
            for (int i = 0; i < 400; i++)
                dataLengths.Add(i);

            foreach (int length in dataLengths)
            {
                CompressionDecompressionTest(length);
            }
        }

        [Fact]
        public void CompressionDecompressionTestShortRandom()
        {
            List<int> dataLengths = [];
            for (int i = 0; i < 400; i++)
                dataLengths.Add(i);

            foreach (int length in dataLengths)
            {
                CompressionDecompressionTest(length, randomData: true);
            }
        }

        [Fact]
        public void CompressionDecompressionZeroLength()
        {
            CompressionDecompressionTest(0);
        }

        private void CompressionDecompressionTest(int dataLength, bool randomData = false)
        {
            var uncompressed = new byte[dataLength];
            if (randomData)
                _random.NextBytes(uncompressed);

            var compressed = MiniLZO.Compress(uncompressed);
            var decompressed = MiniLZO.Decompress(compressed, uncompressed.Length);

            Assert.Equal<byte>(uncompressed, decompressed);
        }
    }
}