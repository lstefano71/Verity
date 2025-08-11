### Verity v1.0 - TODO List

**Phase 1: Project Scaffolding & Core Infrastructure**

- [x] Create .NET project and solution files.
- [x] Rename project and assembly to `Verity`.
- [x] Integrate `Nerdbank.GitVersion` for automatic versioning.
- [x] Integrate `System.CommandLine` and define all arguments/options (`checksumFile`, `--root`, `--algorithm`).
- [x] Integrate `Spectre.Console` dependency.
- [x] Define core data models (`CliOptions`, `ChecksumEntry`, `VerificationResult`, `ResultStatus`, `FinalSummary`).

**Phase 2: Core Verification Logic (The "Engine")**

- [x] Implement the producer-consumer pipeline using `System.Threading.Channels`.
- [ ] **Producer:** Implement logic to read the manifest file line-by-line and push `ChecksumEntry` jobs to the channel.
- [ ] **Consumer:** Implement the worker task logic.
  - [ ] Read a file from disk based on the job's relative path and the root directory.
  - [ ] Compute the hash for the file's content using the specified algorithm.
  - [ ] Handle file-not-found exceptions.
  - [ ] Handle file read/access exceptions (permissions, locks).
- [ ] **Status Logic:** Implement the classification logic for each result.
  - [ ] Compare computed vs. expected hash.
  - [ ] Compare file `LastWriteTimeUtc` against the manifest file's timestamp for mismatch classification (Error vs. Warning).
- [ ] Implement post-processing logic to find files on disk that are not listed in the manifest.

**Phase 3: User & Machine Interface**

- [x] **TUI (stdout):** Display startup banner with `Verity` name and version.
- [x] **TUI (stdout):** Implement `Spectre.Console` `Progress` display and hook it into the verification pipeline.
- [x] **TUI (stdout):** Implement the final summary report (Success/Warning/Error counts, time, throughput).
- [x] **TUI (stdout):** Implement the detailed diagnostic `Table` for displaying all issues.
- [ ] **Machine Report (stderr):** Implement the TSV-formatted report generation for all issues.
  - [ ] Ensure the report is only written to `stderr`.
  - [ ] Ensure the report is only generated if there is at least one issue.
- [x] **Exit Codes:** Implement the final return value logic (`0`, `1`, `-1`) based on the summary.

**Phase 4: Testing & Quality Assurance**

- [ ] **Unit Tests:**
  - [ ] Create tests for manifest line parsing (valid, invalid, empty).
  - [ ] Create tests for status classification logic using mock file system info.
- [ ] **Manual Integration Tests:**
  - [ ] Prepare a test dataset with a mix of large/small files and deep directory structures.
  - [ ] Create a test case for every status type: Success, Error (mismatch), Error (missing), Warning (newer), Warning (unreadable), Warning (unlisted).
  - [ ] Run `Verity.exe` and verify TUI output is correct.
  - [ ] Run `Verity.exe > NUL` and verify `stderr` contains the correct TSV report.
  - [ ] Verify process exit codes in PowerShell (`$LASTEXITCODE`) for all three outcomes.
- [ ] **Performance Test:** Run against a large (100k+ files, 500GB+) dataset and monitor CPU/Disk usage to ensure parallelism is effective.

**Phase 5: Build & Release**

- [ ] Verify successful Native AOT compilation via `dotnet publish`.
- [ ] Write the `README.md` file including:
  - [ ] Project purpose.
  - [ ] CLI usage and examples.
  - [ ] Explanation of output formats (TUI and TSV).
  - [ ] Explanation of exit codes.
- [ ] Create a GitHub repository for the project.
- [ ] Create the v1.0 GitHub Release.
- [ ] Attach the compiled `Verity.exe` as a release asset.
