using FluentAssertions;

using Verity.Utilities;

public class FileIOUtilsTests
{
  [Theory]
  [InlineData(1024, 4096)] // 1KB, expect default buffer size
  [InlineData(64 * 1024, 4096)] // 64KB, threshold, expect default buffer size
  [InlineData(64 * 1024 + 1, 1048576)] // just above threshold, expect large buffer size
  [InlineData(10 * 1024 * 1024, 1048576)] // 10MB, expect large buffer size
  [InlineData(0, 4096)] // 0 bytes, expect default buffer size
  public void GetOptimalBufferSize_ReturnsExpected(int fileSize, int expectedBufferSize)
  {
    var result = FileIOUtils.GetOptimalBufferSize(fileSize);
    result.Should().Be(expectedBufferSize);
  }
}
