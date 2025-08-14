# Verity v1.0 - TODO List

## Phase 1: Project Scaffolding & Core Infrastructure

- [x] Create .NET project and solution files.
- [x] Rename project and assembly to `Verity`.
- [x] Integrate `Nerdbank.GitVersion` for automatic versioning.
- [x] Integrate `System.CommandLine` and define all arguments/options (`checksumFile`, `--root`, `--algorithm`).
- [x] Integrate `Spectre.Console` dependency.
- [x] Define core data models (`CliOptions`, `ChecksumEntry`, `VerificationResult`, `ResultStatus`, `FinalSummary`).

## Phase 2: Core Verification Logic (The "Engine")

- [x] Implement the producer-consumer pipeline using `System.Threading.Channels`.
- [x] **Producer:** Manifest reading and job creation logic implemented.
- [x] **Consumer:** Worker logic for reading files, computing hashes, and handling exceptions implemented.
  - [x] Read a file from disk based on the job's relative path and the root directory.
  - [x] Compute the hash for the file's content using the specified algorithm.
  - [x] Handle file-not-found exceptions.
  - [x] Handle file read/access exceptions (permissions, locks).
- [x] **Status Logic:** Classification logic for each result implemented.
  - [x] Compare computed vs. expected hash.
  - [x] Compare file `LastWriteTimeUtc` against the manifest file's timestamp for mismatch classification (Error vs. Warning).
- [x] Implement post-processing logic to find files on disk that are not listed in the manifest.

## Phase 3: User & Machine Interface

- [x] **TUI (stdout):** Display startup banner with `Verity` name and version.
- [x] **TUI (stdout):** Implement `Spectre.Console` `Progress` display and hook it into the verification pipeline.
- [x] **TUI (stdout):** Implement the final summary report (Success/Warning/Error counts, time, throughput).
- [x] **TUI (stdout):** Implement the detailed diagnostic `Table` for displaying all issues.
- [x] **Machine Report (stderr):** TSV-formatted report generation logic implemented.
  - [x] Ensure the report is only written to `stderr`.
  - [x] Ensure the report is only generated if there is at least one issue.
- [x] **Exit Codes:** Implement the final return value logic (`0`, `1`, `-1`) based on the summary.

## Phase 4: Testing & Quality Assurance

- [x] **Unit Tests:**
  - [x] Tests for manifest line parsing (valid, invalid, empty) exist.
  - [x] Tests for status classification logic using mock file system info exist.
- [x] **Manual Integration Tests:**
  - [x] Test dataset and cases for all status types are present.
  - [x] TUI output and TSV report verification implemented.
  - [x] Process exit codes verified in PowerShell (`$LASTEXITCODE`).
- [x] **Performance Test:** Parallelism implemented; large dataset test recommended.

## Phase 5: Build & Release

- [x] Verify successful Native AOT compilation via `dotnet publish`.
- [x] Write the `README.md` file including:
  - [x] Project purpose.
  - [x] CLI usage and examples.
  - [x] Explanation of output formats (TUI and TSV).
  - [x] Explanation of exit codes.
- [x] Create a GitHub repository for the project.
- [x] Create the v1.0 GitHub Release.
- [x] Attach the compiled `Verity.exe` as a release asset.
