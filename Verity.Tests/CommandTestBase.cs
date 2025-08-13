public abstract class CommandTestBase : IAsyncLifetime
{
  protected readonly CommonTestFixture fixture;
  public CommandTestBase(CommonTestFixture fixture)
  {
    this.fixture = fixture;
  }

  public async Task InitializeAsync()
  {
    if (Directory.Exists(fixture.TempDir)) {
      Directory.Delete(fixture.TempDir, true);
    }
    Directory.CreateDirectory(fixture.TempDir);
    await Task.CompletedTask;
  }

  public async Task DisposeAsync()
  {
    await Task.CompletedTask;
  }
}
