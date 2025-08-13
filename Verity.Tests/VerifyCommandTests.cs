using FluentAssertions;

public class VerifyCommandTests(VerityTestFixture fixture) : IClassFixture<VerityTestFixture>
{
  readonly VerityTestFixture fixture = fixture;

  [Fact]
  public async Task Verify_Success()
  {
    var file = fixture.CreateTestFile("a.txt", "hello");
    var hash = "5d41402abc4b2a76b9719d911017c592"; // md5 of "hello"
    var manifest = $"{hash}\ta.txt";
    fixture.CreateTestFile("manifest.txt", manifest);

    var result = await fixture.RunVerity($"verify --manifest manifest.txt");
    result.ExitCode.Should().Be(0);
    result.StdErr.Should().BeEmpty();
  }

  [Fact]
  public async Task Verify_FileNotFound()
  {
    var manifest = $"deadbeef\tnotfound.txt";
    fixture.CreateTestFile("manifest.txt", manifest);

    var result = await fixture.RunVerity($"verify --manifest manifest.txt");
    result.ExitCode.Should().Be(-1);
    result.StdErr.Should().Contain("ERROR");
  }

  [Fact]
  public async Task Verify_HashMismatch_Error()
  {
    var file = fixture.CreateTestFile("a.txt", "hello");
    var manifest = $"deadbeef\ta.txt";
    fixture.CreateTestFile("manifest.txt", manifest);

    var result = await fixture.RunVerity($"verify --manifest manifest.txt");
    result.ExitCode.Should().Be(-1);
    result.StdErr.Should().Contain("ERROR");
  }

  [Fact]
  public async Task Verify_HashMismatch_Warning()
  {
    var file = fixture.CreateTestFile("a.txt", "hello");
    System.Threading.Thread.Sleep(1100); // ensure file is newer
    var manifest = $"deadbeef\ta.txt";
    fixture.CreateTestFile("manifest.txt", manifest);

    var result = await fixture.RunVerity($"verify --manifest manifest.txt");
    result.ExitCode.Should().Be(1);
    result.StdErr.Should().Contain("WARNING");
  }

  [Fact]
  public async Task Verify_UnlistedFile_Warning()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateTestFile("manifest.txt", $"deadbeef\ta.txt");
    fixture.CreateTestFile("extra.txt", "extra");

    var result = await fixture.RunVerity($"verify --manifest manifest.txt");
    result.ExitCode.Should().Be(1);
    result.StdErr.Should().Contain("WARNING");
  }

  [Fact]
  public async Task Verify_GlobFiltering()
  {
    fixture.CreateTestFile("a.txt", "hello");
    fixture.CreateTestFile("b.log", "log");
    fixture.CreateTestFile("manifest.txt", $"deadbeef\ta.txt\nbeefdead\tb.log");

    var result = await fixture.RunVerity($"verify --manifest manifest.txt --include \"*.txt\"");
    result.StdErr.Should().NotContain("b.log");
  }
}