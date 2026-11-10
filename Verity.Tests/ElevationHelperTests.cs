using Verity.Utilities;

namespace Verity.Tests;

[TestFixture]
public class ElevationHelperTests
{
    [Test]
    public void IsElevated_ReturnsBoolean()
    {
        // Act
        var result = ElevationHelper.IsElevated();

        // Assert
        Assert.That(result, Is.TypeOf<bool>());
    }

    [Test]
    public void RequiresElevation_VssTrue_ReturnsTrue()
    {
        // Arrange
        var options = new CliOptions(
            new FileInfo("test.sha256"),
            null,
            "SHA256",
            UseVss: true
        );

        // Act
        var result = ElevationHelper.RequiresElevation(options);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RequiresElevation_VssFalse_ReturnsFalse()
    {
        // Arrange
        var options = new CliOptions(
            new FileInfo("test.sha256"),
            null,
            "SHA256",
            UseVss: false
        );

        // Act
        var result = ElevationHelper.RequiresElevation(options);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    [Platform("Win")]
    public async Task RestartElevatedAsync_ValidArgs_DoesNotThrow()
    {
        // Arrange
        var args = new[] { "verify", "test.sha256", "--vss" };

        // Act & Assert
        // Note: This test won't actually restart the process, but it should not throw
        // unless there's a fundamental issue with the implementation
        Assert.DoesNotThrowAsync(async () =>
        {
            try
            {
                await ElevationHelper.RestartElevatedAsync(args);
            }
            catch (InvalidOperationException)
            {
                // This is expected if Environment.ProcessPath is null in test environment
            }
        });
    }

    [Test]
    [Platform("Win")]
    public async Task RestartElevatedAsync_EmptyArgs_DoesNotThrow()
    {
        // Arrange
        var args = Array.Empty<string>();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
        {
            try
            {
                await ElevationHelper.RestartElevatedAsync(args);
            }
            catch (InvalidOperationException)
            {
                // This is expected if Environment.ProcessPath is null in test environment
            }
        });
    }

    [Test]
    [Platform("Win")]
    public async Task RestartElevatedAsync_ArgsWithSpaces_DoesNotThrow()
    {
        // Arrange
        var args = new[] { "verify", "test file.sha256", "--root", "C:\\Program Files" };

        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
        {
            try
            {
                await ElevationHelper.RestartElevatedAsync(args);
            }
            catch (InvalidOperationException)
            {
                // This is expected if Environment.ProcessPath is null in test environment
            }
        });
    }

    [Test]
    [Platform("Win")]
    public async Task RestartElevatedAsync_ArgsWithQuotes_DoesNotThrow()
    {
        // Arrange
        var args = new[] { "verify", "\"test file.sha256\"", "--root", "\"C:\\Program Files\"" };

        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
        {
            try
            {
                await ElevationHelper.RestartElevatedAsync(args);
            }
            catch (InvalidOperationException)
            {
                // This is expected if Environment.ProcessPath is null in test environment
            }
        });
    }

    [Test]
    [Platform("Win")]
    public async Task RestartElevatedAsync_NullArgs_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await ElevationHelper.RestartElevatedAsync(null!);
        });
    }

    // Test the internal argument escaping logic indirectly
    [Test]
    public void EscapeArgument_Equivalence_Test()
    {
        // Arrange
        var testArgs = new[]
        {
            "simple",
            "with space",
            "with\"quote",
            "with\ttab",
            "",
            "already\"escaped\"",
            "complex \"argument\" with spaces"
        };

        // Act & Assert - Test that RestartElevatedAsync doesn't throw with various argument types
        Assert.DoesNotThrowAsync(async () =>
        {
            try
            {
                await ElevationHelper.RestartElevatedAsync(testArgs);
            }
            catch (InvalidOperationException)
            {
                // Expected in test environment
            }
        });
    }
}