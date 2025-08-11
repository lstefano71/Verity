### Product Requirements Document (PRD)

**Product Name:** Verity

**Version:** 1.0

**Status:** Proposed

---

### 1. Introduction & Problem Statement

System administrators, data archivists, and power users need to reliably verify the integrity of large and numerous files, a process often triggered by data migration, backup restoration, or routine checks against silent data corruption ("bit rot"). Current command-line tools for this task are often slow due to single-threaded processing, provide inadequate real-time feedback for long-running jobs, and lack a clear, dual-purpose reporting mechanism for both human operators and automated scripts.

Verity is a purpose-built console utility designed to provide an extremely fast, parallelized, and ergonomic file integrity verification experience on modern Windows systems.

### 2. Goals & Objectives

* **Primary Goal:** To be the fastest and most ergonomic tool for verifying file checksums against a manifest file on Windows.
* **Objective 1: Performance.** Maximize hardware utilization by parallelizing I/O (file reading) and CPU-bound (hashing) operations to significantly reduce verification time for large datasets.
* **Objective 2: User Experience.** Provide clear, real-time feedback to the user during operation via a rich Terminal User Interface (TUI), and provide an ergonomic, intuitive command-line structure.
* **Objective 3: Automation.** Enable seamless integration into automated scripts and CI/CD pipelines through distinct process exit codes and a dedicated machine-readable error report stream.
* **Objective 4: Deployment.** Deliver the tool as a single, self-contained, high-performance executable with no external runtime dependencies.

### 3. Target Audience

* **System Administrators:** Verifying server backups, data migrations, or application deployments.
* **Data Archivists / "Data Hoarders":** Performing periodic integrity checks on large archives of personal or professional data (photos, videos, documents).
* **Developers & DevOps Engineers:** Ensuring build artifacts or datasets have not been corrupted during transfer or storage.
* **Power Users:** Verifying the integrity of large file downloads or local file transfers.

### 4. Functional Requirements

| ID    | Requirement                                                                                                                                                                                                                                                                                                                      |
| :---- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **FR-1**  | **Core Verification Logic**                                                                                                                                                                                                                                                                                                      |
| FR-1.1 | The tool shall accept a path to a checksum manifest file as its primary input.                                                                                                                                                                                                                                                       |
| FR-1.2 | The manifest file format must be plain text. Each non-empty line must contain a lowercase hexadecimal hash, followed by a single tab character (`\t`), followed by a relative file path. The tool should be resilient to malformed lines.                                                                                           |
| FR-1.3 | For each entry, the tool shall compute the hash of the corresponding file on disk and perform a case-insensitive comparison against the expected hash from the manifest.                                                                                                                                                             |
| **FR-2**  | **Command-Line Interface (CLI)**                                                                                                                                                                                                                                                                                                 |
| FR-2.1 | The application executable shall be named `Verity.exe`.                                                                                                                                                                                                                                                                          |
| FR-2.2 | **`checksumFile` (Argument, Required):** A positional argument specifying the path to the manifest file. The tool shall exit with an error if this file does not exist.                                                                                                                                                             |
| FR-2.3 | **`--root` (Option, Optional):** An option to specify the root directory against which relative paths in the manifest are resolved. If omitted, this defaults to the directory containing the `checksumFile`. The tool shall exit with an error if the specified directory does not exist.                                          |
| FR-2.4 | **`--algorithm` (Option, Optional):** An option to specify the hashing algorithm. This defaults to `SHA256`. The name must correspond to a valid algorithm name supported by the .NET cryptography libraries.                                                                                                                           |
| **FR-3**  | **Status Classification Logic**                                                                                                                                                                                                                                                                                                  |
| FR-3.1 | The last modification timestamp of the checksum manifest file shall be used as a baseline for classifying certain failures.                                                                                                                                                                                                        |
| FR-3.2 | **Success:** The file exists and its computed hash matches the expected hash.                                                                                                                                                                                                                                                        |
| FR-3.3 | **Error:** The file listed in the manifest does not exist on disk.                                                                                                                                                                                                                                                                 |
| FR-3.4 | **Error:** The file's computed hash does not match the expected hash, AND the file's last modification time is older than or equal to the manifest's last modification time.                                                                                                                                                           |
| FR-3.5 | **Warning:** The file's computed hash does not match the expected hash, BUT the file's last modification time is newer than the manifest's last modification time.                                                                                                                                                                    |
| FR-3.6 | **Warning:** The file exists but cannot be read due to permissions, file locks, or other I/O errors.                                                                                                                                                                                                                                |
| FR-3.7 | **Warning:** A file is found on disk within the root directory structure that is *not* listed in the manifest. The manifest file itself shall be ignored during this check.                                                                                                                                                            |
| **FR-4**  | **Human-Friendly Output (TUI on `stdout`)**                                                                                                                                                                                                                                                                                      |
| FR-4.1 | On startup, the tool shall display its name and version number (e.g., `Verity v1.0.1-beta.4`).                                                                                                                                                                                                                                     |
| FR-4.2 | During operation, a live-updating progress display shall show percentage complete, estimated time remaining, and overall data throughput (e.g., in MB/s).                                                                                                                                                                           |
| FR-4.3 | Upon completion, a summary report shall be printed to `stdout` containing the total count of successes, warnings, and errors, along with the total elapsed time.                                                                                                                                                                    |
| FR-4.4 | If warnings or errors occurred, a detailed, color-coded diagnostic table shall be printed to `stdout`, listing the status, file path, details, and expected/actual hashes for each issue.                                                                                                                                             |
| **FR-5**  | **Machine-Readable Output (`stderr`)**                                                                                                                                                                                                                                                                                           |
| FR-5.1 | If, and only if, one or more warnings or errors are generated, a report shall be printed to the standard error (`stderr`) stream.                                                                                                                                                                                                   |
| FR-5.2 | The report format shall be Tab-Separated Values (TSV). The first line shall be a header: `#Status\tFile\tDetails\tExpectedHash\tActualHash`. Subsequent lines will contain the data for each issue.                                                                                                                                      |
| FR-5.3 | The `stdout` stream must remain free of machine-readable report data, and `stderr` must remain free of TUI formatting escape codes.                                                                                                                                                                                                   |
| **FR-6**  | **Process Exit Codes**                                                                                                                                                                                                                                                                                                         |
| FR-6.1 | `0`: Success. All files were verified successfully with no warnings or errors.                                                                                                                                                                                                                                                     |
| FR-6.2 | `1`: Warning. The verification completed, but one or more warnings were generated. There were no errors.                                                                                                                                                                                                                          |
| FR-6.3 | `-1` (or system equivalent): Error. The verification completed, and one or more errors were generated.                                                                                                                                                                                                                               |

### 5. Non-Functional Requirements

| ID     | Requirement                                                                                                                                                                            |
| :----- | :--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| NFR-1  | **Performance:** The file processing pipeline must be concurrent, using a producer-consumer model to scale with available CPU cores and keep the I/O subsystem saturated.                  |
| NFR-2  | **Packaging:** The tool must be compilable using Native AOT into a single, self-contained `.exe` file for Windows, requiring no external .NET runtime installation.                        |
| NFR-3  | **Versioning:** The assembly and displayed version number must be automatically and deterministically generated from the project's Git history.                                            |
| NFR-4  | **Resource Management:** The application must operate within reasonable memory limits and not read the entire content of all files into memory at once.                                    |
| NFR-5  | **OS Target:** The primary target operating system is modern Windows (Windows 10 / Server 2016 and newer). Cross-platform compatibility is not a requirement for v1.0.                         |

### 6. Future Work / Out of Scope for v1.0

* **Creation Mode:** A feature (`verity create`) to scan a directory and generate a new checksum manifest file.
* **VSS Integration:** Using the Windows Volume Shadow Copy Service (VSS) to read from a consistent point-in-time snapshot, avoiding issues with file locks from running programs.
* **Update/Repair Functionality:** The tool is strictly read-only and will not modify, add, or delete any files.
* **Alternate Manifest Formats:** Support for formats other than the specified `hash<tab>path` is not planned.
* **Interactive Mode:** An interactive TUI for resolving conflicts is not in scope.
