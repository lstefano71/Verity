public class StatusClassifierTests
{
  [Fact]
  public void Classify_WhenHashesMatch_ReturnsSuccess()
  {
    var result = StatusClassifier.Classify("abc", "abc", DateTime.UtcNow, DateTime.UtcNow);
    Assert.Equal(ResultStatus.Success, result);
  }

  [Fact]
  public void Classify_WhenHashesMismatchAndFileIsOlder_ReturnsError()
  {
    var manifestTime = DateTime.UtcNow;
    var fileTime = manifestTime.AddMinutes(-10);
    var result = StatusClassifier.Classify("abc", "def", fileTime, manifestTime);
    Assert.Equal(ResultStatus.Error, result);
  }

  [Fact]
  public void Classify_WhenHashesMismatchAndFileIsSameAge_ReturnsError()
  {
    var now = DateTime.UtcNow;
    var result = StatusClassifier.Classify("abc", "def", now, now);
    Assert.Equal(ResultStatus.Error, result);
  }

  [Fact]
  public void Classify_WhenHashesMismatchAndFileIsNewer_ReturnsWarning()
  {
    var manifestTime = DateTime.UtcNow;
    var fileTime = manifestTime.AddMinutes(10);
    var result = StatusClassifier.Classify("abc", "def", fileTime, manifestTime);
    Assert.Equal(ResultStatus.Warning, result);
  }

  [Theory]
  [InlineData(null, "abc", ResultStatus.Error)]
  [InlineData("abc", null, ResultStatus.Error)]
  [InlineData("", "abc", ResultStatus.Error)]
  [InlineData("abc", "", ResultStatus.Error)]
  public void Classify_NullOrEmptyHashes_ReturnsError(string? expected, string? actual, ResultStatus expectedStatus)
  {
    var manifestTime = DateTime.UtcNow;
    var fileTime = manifestTime.AddMinutes(-10);
    var result = StatusClassifier.Classify(expected, actual, fileTime, manifestTime);
    Assert.Equal(expectedStatus, result);
  }

  [Fact]
  public void Classify_ExtremeDates()
  {
    var result = StatusClassifier.Classify("abc", "def", DateTime.MaxValue, DateTime.MinValue);
    Assert.Equal(ResultStatus.Warning, result);

    result = StatusClassifier.Classify("abc", "def", DateTime.MinValue, DateTime.MaxValue);
    Assert.Equal(ResultStatus.Error, result);
  }
}