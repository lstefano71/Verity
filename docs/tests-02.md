# **Implementation Plan: TSV Report Testing**

#### **1. Goal & Strategy**

The primary goal is to validate that Verity's TSV reporting is **accurate, correctly formatted, and properly routed** under all relevant conditions. We will treat the TSV output format as a public contract that must not be broken.

Our strategy is to extend the integration tests to:

1. Execute `Verity.exe` for scenarios that produce warnings and errors.
2. Capture the TSV output, whether it's from `stderr` or a designated file.
3. Parse the TSV content.
4. Perform detailed assertions on the parsed data to confirm its correctness.
5. Validate the negative case (i.e., that no report is generated on a successful run).

#### **2. Enhancing the Test Fixture**

The `VerityTestFixture` is the perfect place to centralize the logic for running the process and parsing the output.

**A. Refine `ProcessResult` Record**

Ensure the `ProcessResult` helper record returned by the `RunVerity` method captures everything we need:

```csharp
// In VerityTestFixture.cs
public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
```

**B. Create a TSV Parsing Helper**

To avoid repetitive parsing code in every test, we'll create a helper model and a parser.

1. **Create a `TsvReportRow` Record:** This will represent a single parsed row from the report, making assertions clean and strongly-typed.

    ```csharp
    // In a new file, TsvReportRow.cs, within the test project
    public record TsvReportRow(string Status, string File, string Details, string ExpectedHash, string ActualHash);
    ```

2. **Create the Parser Method:** Add this static method to a new helper class or within the fixture itself.

    ```csharp
    // In a new helper class, e.g., TsvReportParser.cs
    public static class TsvReportParser
    {
        public static List<TsvReportRow> Parse(string tsvContent)
        {
            var rows = new List<TsvReportRow>();
            var lines = tsvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Skip the header line if it exists
            var dataLines = lines.Where(l => !l.StartsWith("#"));

            foreach (var line in dataLines)
            {
                var parts = line.Split('\t');
                if (parts.Length == 5)
                {
                    rows.Add(new TsvReportRow(
                        Status: parts[0],
                        File: parts[1],
                        Details: parts[2],
                        ExpectedHash: parts[3],
                        ActualHash: parts[4]
                    ));
                }
            }
            return rows;
        }
    }
    ```

#### **3. Implementation: Test Scenarios**

We will now add or modify tests within `VerifyCommandTests.cs` to cover the following scenarios.

**Scenario 1: Testing `stderr` Output (Default Behavior)**

This tests the primary use case for scripting, where output is piped from `stderr`.

* **Test Name:** `Verify_WithMissingFile_WritesCorrectTsvToStdErr`
* **Arrange:**
  * Use the fixture to create `file1.txt` and `file2.txt`.
  * Run `Verity create` to generate a valid `manifest.sha256`.
  * Delete `file1.txt` using `fixture.DeleteTestFile("file1.txt")`.
* **Act:**
  * Run `var result = fixture.RunVerity("verify manifest.sha256");`
* **Assert:**
  * `result.ExitCode.Should().Be(-1);`
  * `result.StandardError.Should().NotBeNullOrWhiteSpace();`
  * The first line of `result.StandardError` should be `"#Status\tFile\tDetails\tExpectedHash\tActualHash"`.
  * Parse the output: `var reportRows = TsvReportParser.Parse(result.StandardError);`
  * `reportRows.Should().HaveCount(1);`
  * `var row = reportRows.Single();`
  * `row.Status.Should().Be("ERROR");`
  * `row.File.Should().Be(fixture.GetFullPath("file1.txt"));`
  * `row.Details.Should().Be("File not found.");`
  * `row.ExpectedHash.Should().NotBeEmpty();`
  * `row.ActualHash.Should().BeEmpty();`

**Scenario 2: Testing `--tsv-report` File Output**

This validates that output is correctly redirected to a file and that `stderr` remains clean.

* **Test Name:** `Verify_WithMultipleIssues_WritesCorrectTsvToFile`
* **Arrange:**
  * Create `file1.txt`, `file2.txt`.
  * Run `create` to make `manifest.sha256`.
  * Delete `file1.txt`.
  * Modify `file2.txt` so it will have a hash mismatch and be classified as a "newer" warning.
  * Create a new, unlisted file `extra.txt`.
  * Define the report path: `var reportPath = fixture.GetFullPath("error-report.tsv");`
* **Act:**
  * Run `var result = fixture.RunVerity($"verify manifest.sha256 --tsv-report \"{reportPath}\"");`
* **Assert:**
  * `result.ExitCode.Should().Be(-1);` (An Error exists, so the exit code is -1).
  * `result.StandardError.Should().BeEmpty();` **(Crucial check)**
  * `File.Exists(reportPath).Should().BeTrue();`
  * Read the file content: `var fileContent = File.ReadAllText(reportPath);`
  * Parse the content: `var reportRows = TsvReportParser.Parse(fileContent);`
  * `reportRows.Should().HaveCount(3);`
  * Assert the contents of each of the 3 rows (one ERROR for missing, one WARNING for newer mismatch, one WARNING for unlisted file), checking their `Status`, `File`, and `Details` fields.

**Scenario 3: The "Happy Path" - No Report Generation**

This ensures that no empty or erroneous reports are created when everything is successful.

* **Test Name:** `Verify_WithSuccessfulRun_DoesNotWriteTsvToStdErr`
* **Arrange:**
  * Create a valid file set and manifest.
* **Act:**
  * Run `var result = fixture.RunVerity("verify manifest.sha256");`
* **Assert:**
  * `result.ExitCode.Should().Be(0);`
  * `result.StandardError.Should().BeEmpty();`

* **Test Name:** `Verify_WithSuccessfulRunAndTsvOption_DoesNotCreateReportFile`
* **Arrange:**
  * Create a valid file set and manifest.
  * Define the report path: `var reportPath = fixture.GetFullPath("report.tsv");`
* **Act:**
  * Run `var result = fixture.RunVerity($"verify manifest.sha256 --tsv-report \"{reportPath}\"");`
* **Assert:**
  * `result.ExitCode.Should().Be(0);`
  * `File.Exists(reportPath).Should().BeFalse();` (The application should be smart enough not to create an empty report file).
