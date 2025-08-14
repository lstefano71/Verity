# **Implementation Plan: Verity Test Suite**

#### **1. Guiding Principles & Technology Stack**

* **Test Framework:** **xUnit** will be used as the primary test framework. It is modern, flexible, and well-supported in the .NET ecosystem.
* **Assertion Library:** **xUnit.Assert** will be used for test assertions (e.g., `Assert.Equal(5, result)`).
* **Mocking Library:** **NSubstitute** will be used for creating mocks and stubs when isolating components for unit tests. It is known for its simple and clean syntax.
* **Separation of Concerns:**
  * **Unit Tests** will focus on individual methods and classes in isolation, without accessing the file system or external processes. They will be fast and numerous.
  * **Integration Tests** will test the application as a whole, running the compiled `Verity.exe` and interacting with a real (but temporary) file system to validate end-to-end workflows, command-line argument parsing, and output streams.

#### **2. Phase 1: Test Project Setup**

1. **Create a New Test Project:**
    * In the solution directory, run: `dotnet new xunit -n Verity.Tests`
    * Add the new project to the solution: `dotnet sln add Verity.Tests/Verity.Tests.csproj`

2. **Add Project Reference:**
    * Reference the main Verity project from the test project:
        `dotnet add Verity.Tests/Verity.Tests.csproj reference Verity.csproj`

3. **Install Necessary NuGet Packages:**
    * Navigate to the `Verity.Tests` directory.
    * Run the following commands:

        ```shell
        
        
        ```

#### **3. Phase 2: Unit Test Implementation**

The goal of this phase is to test individual, isolated components, focusing on business logic rather than I/O.

**A. `Utilities/GlobUtils.cs`**

* **Target:** `NormalizeGlobs`, `IsMatch`
* **Test Class:** `GlobUtilsTests.cs`
* **Test Cases for `NormalizeGlobs`:**
  * `NormalizeGlobs_WithNullInput_ReturnsDefault` (should return `["**/*"]` for include, `[]` for exclude).
  * `NormalizeGlobs_WithEmptyInput_ReturnsDefault`.
  * `NormalizeGlobs_WithMultiplePatterns_SplitsAndTrimsCorrectly`.
* **Test Cases for `IsMatch`:**
  * `IsMatch_WithSimpleInclude_ReturnsTrueForMatch`.
  * `IsMatch_WithSimpleInclude_ReturnsFalseForNoMatch`.
  * `IsMatch_WithExcludePattern_ReturnsFalseForMatch`.
  * `IsMatch_WhenBothIncludeAndExcludeMatch_ReturnsFalse`.
  * `IsMatch_WithRecursiveGlob_MatchesNestedFile`.
  * `IsMatch_WithDirectoryGlob_MatchesCorrectly`.
  * `IsMatch_IsCaseInsensitive`.

**B. `Utilities/Utilities.cs`**

* **Target:** `AbbreviatePathForDisplay`
* **Test Class:** `UtilitiesTests.cs`
* **Test Cases for `AbbreviatePathForDisplay`:**
  * `AbbreviatePath_WhenPathIsShort_ReturnsUnchangedPath`.
  * `AbbreviatePath_WhenPathIsLong_TruncatesCorrectly`.
  * `AbbreviatePath_WithLongDirectoryAndShortFile_PrioritizesFileName`.
  * `AbbreviatePath_WithVeryLongSegments_ReturnsMaxLengthString`.

**C. `Services/VerificationService.cs` (Status Logic)**

* **Challenge:** The core methods are tightly coupled to the file system. We will extract and test the core classification logic separately.
* **Refactoring Step:** Create a new internal static class `StatusClassifier` with a method:

    ```csharp
    public static ResultStatus Classify(string expectedHash, string actualHash, DateTime fileWriteTime, DateTime manifestWriteTime)
    ```

* **Test Class:** `StatusClassifierTests.cs`
* **Test Cases:**
  * `Classify_WhenHashesMatch_ReturnsSuccess`.
  * `Classify_WhenHashesMismatchAndFileIsOlder_ReturnsError`.
  * `Classify_WhenHashesMismatchAndFileIsSameAge_ReturnsError`.
  * `Classify_WhenHashesMismatchAndFileIsNewer_ReturnsWarning`.

**D. `Utilities/ManifestReader.cs` (Parsing Logic)**

* **Challenge:** `ReadEntriesAsync` is tied to the file system.
* **Refactoring Step:** Extract the line parsing logic into a testable public static method within the class.

    ```csharp
    public static ManifestEntry? ParseLine(string line)
    ```

* **Test Class:** `ManifestReaderTests.cs`
* **Test Cases:**
  * `ParseLine_WithValidLine_ReturnsCorrectEntry`.
  * `ParseLine_WithMalformedLine_ReturnsNull` (e.g., no tab).
  * `ParseLine_WithEmptyOrWhitespaceLine_ReturnsNull`.
  * `ParseLine_WithExtraTabs_ParsesFirstTwoParts`.

#### **4. Phase 3: Thorough Integration Test Implementation**

This phase will use a test fixture to create a temporary, controlled file system environment and then execute the compiled `Verity.exe` to validate end-to-end behavior.

**A. Create the Integration Test Fixture**

* **File:** `VerityTestFixture.cs`
* **Purpose:** Manages the lifecycle of a temporary directory for testing. It will be created before tests run and destroyed after.
* **Implementation:**
  * Implement `IAsyncLifetime` (or `IDisposable`).
  * In the constructor (or `InitializeAsync`), create a unique temporary directory in `Path.GetTempPath()`.
  * In `DisposeAsync` (or `Dispose`), delete the temporary directory.
  * Provide helper methods:
    * `string CreateTestFile(string relativePath, string content)`
    * `void ModifyTestFile(string relativePath, string newContent)`
    * `void DeleteTestFile(string relativePath)`
    * `string GetFullPath(string relativePath)`
    * `ProcessResult RunVerity(string args)`: A helper to start `Verity.exe`, wait for exit, and return the exit code, `stdout`, and `stderr`.

**B. `VerifyCommandTests.cs`**

* Use the fixture: `public class VerifyCommandTests : IClassFixture<VerityTestFixture>`
* **Scenarios:**
  * **Success:** Create files, run `create`, then run `verify`. **Assert:** Exit code is `0`. `stderr` is empty.
  * **File Not Found:** Create a manifest, delete one of the files, run `verify`. **Assert:** Exit code is `-1`. `stderr` contains one `ERROR` line for the missing file.
  * **Hash Mismatch (Error):** Create a manifest, modify a file *without* changing its timestamp (or set it to be older), run `verify`. **Assert:** Exit code is `-1`. `stderr` contains one `ERROR` line for the mismatch.
  * **Hash Mismatch (Warning):** Create a manifest, wait a moment, modify a file (so its timestamp is newer), run `verify`. **Assert:** Exit code is `1`. `stderr` contains one `WARNING` line for the "newer" mismatch.
  * **Unlisted File (Warning):** Create a manifest, add a new file to the directory, run `verify`. **Assert:** Exit code is `1`. `stderr` contains one `WARNING` for the unlisted file.
  * **Glob Filtering:** Create a manifest with `a.txt` and `b.log`. Run `verify --include "*.txt"`. **Assert:** The unlisted `b.log` does **not** trigger a warning because it's filtered out.

**C. `CreateCommandTests.cs`**

* Use the test fixture.
* **Scenarios:**
  * **Basic Creation:** Create a file structure, run `create`. **Assert:** Manifest is created, content is correct (hashes and relative paths). Exit code is `0`.
  * **Empty Directory:** Run `create` on an empty directory. **Assert:** Exit code is `1` (Warning). Manifest is empty.
  * **Glob Filtering:** Create `a.txt`, `b.log`, `c.tmp`. Run `create --include "*.txt;*.log" --exclude "*.tmp"`. **Assert:** Manifest contains `a.txt` and `b.log` but not `c.tmp`.

**D. `AddCommandTests.cs`**

* Use the test fixture.
* **Scenarios:**
  * **Add New Files:** Run `create` on `a.txt`. Then create `b.txt`. Run `add`. **Assert:** The new manifest contains both `a.txt` and `b.txt`. Exit code is `0`.
  * **No New Files:** Run `create`, then run `add` immediately. **Assert:** Exit code is `1` (Warning, no new files). Manifest is unchanged.
  * **Glob Filtering:** Create `a.txt`. Create `b.log` and `c.txt`. Run `add --include "*.log"`. **Assert:** Manifest now contains `a.txt` and `b.log`, but not `c.txt`.

#### **5. Phase 4: CI Integration**

1. **Modify `.github/workflows/release.yml`:**
    * Inside the `build` job, after the `Run Cake build and package` step and before the `Get Release Version` step, add a step to run the tests.
    * This ensures that no release can be created if any tests are failing.

    ```yaml
      - name: Run Cake build and package
        run: dotnet cake --target=Package --configuration=Release

      - name: Run Unit & Integration Tests
        run: dotnet test --configuration Release --no-build --verbosity normal
    ```

    *Note: The `--no-build` flag is used because the previous step already built the project.*

#### **6. Execution Roadmap**

1. **Sprint 1: Foundation.** Complete **Phase 1** and **Phase 2 (A & B)**. This establishes the test project and covers the pure utility functions, which is the lowest-hanging fruit.
2. **Sprint 2: Test Fixture & Core Integration.** Complete **Phase 3 (A)** and the success-path tests for each command in **Phase 3 (B, C, D)**. This builds the core infrastructure for integration testing.
3. **Sprint 3: Failure Scenarios.** Implement all the failure and warning path scenarios in the integration tests. This is crucial for ensuring robust error handling.
4. **Sprint 4: Refactoring & CI.** Implement the small refactorings and corresponding unit tests from **Phase 2 (C & D)**. Complete **Phase 4** by integrating the `dotnet test` command into the GitHub Actions workflow.
