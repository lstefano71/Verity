# Verity

**Verity** is a high-performance, parallelized file integrity verification and manifest creation tool for modern Windows systems. It is designed to be extremely fast, provide excellent real-time feedback, and be easily integrated into automated scripts.

It is delivered as a single, self-contained executable with no external .NET runtime dependencies, built for speed and ease of use.

## Key Features

*   **High Performance:** Utilizes a concurrent producer-consumer architecture to maximize throughput by keeping CPU cores and the I/O subsystem saturated.
*   **Manifest Creation:** Can generate checksum manifests for a directory structure.
*   **Intelligent I/O:** Automatically adjusts file read buffer sizes based on file size to optimize performance for both very large and very small files.
*   **Glob Filtering:** Supports powerful include/exclude glob patterns for file selection.
*   **Rich TUI:** A clean and modern Terminal User Interface powered by Spectre.Console provides live progress, throughput metrics, and a detailed final report.
*   **Automation-Friendly:** Provides distinct exit codes for success, warning, and error states. It also generates a machine-readable TSV (Tab-Separated Values) report on `stderr` or to a file for easy parsing by other tools.
*   **Robust Error Handling:** Differentiates between critical errors (e.g., hash mismatch) and warnings (e.g., mismatch on a newer file, unlisted files), giving the user a clear picture of their data's state.

---

## Manifest Format

Each line: `hash<tab>relative_path` (lowercase hex, tab-separated). Example:

```
820208145359d1c620d459f00784e190    img/IMG_0101.JPG
375d554729e87a93f65cd724bbd29d96    doc/report-final.docx
```

---

## Usage

Verity is a command-line tool with three main commands: `verify`, `create`, and `add`.

### `verify`

Verifies the integrity of files based on a checksum manifest.

```shell
Verity.exe verify <checksumFile> [options]
```

#### Arguments

*   **`checksumFile` (Required):** The path to the manifest file containing the checksums.

#### Options

*   **`--root <directory>` (Optional):** The root directory for the files. If omitted, Verity uses the directory where the `checksumFile` is located.
*   **`--algorithm <name>` (Optional):** The hashing algorithm to use. Recognized by .NET cryptography. If omitted, inferred from manifest extension (`.sha256`, `.md5`, `.sha1`). **Default: `SHA256`**.
*   **`--threads <number>` (Optional):** Number of threads for parallel processing. Defaults to logical processor count.
*   **`--tsvReport <file>` (Optional):** Path to write a machine-readable TSV error report. If omitted, TSV is written to `stderr` if issues are found.
*   **`--showTable` (Optional):** Force the diagnostic table to be shown even if there are no issues.
*   **`--include <patterns>` (Optional):** Semicolon-separated glob patterns for files to include (e.g., `*.jpg;*.docx`). Defaults to all files (`**/*`).
*   **`--exclude <patterns>` (Optional):** Semicolon-separated glob patterns for files to exclude (e.g., `*.tmp;*.bak`).

---

### `create`

Creates a checksum manifest from a directory.

```shell
Verity.exe create <outputManifest> [options]
```

#### Arguments

*   **`outputManifest` (Required):** The path to the manifest file to be created.

#### Options

*   **`--root <directory>` (Optional):** The root directory to scan for files. If omitted, uses manifest's directory.
*   **`--algorithm <name>` (Optional):** Hashing algorithm. If omitted, inferred from manifest extension. **Default: `SHA256`**.
*   **`--threads <number>` (Optional):** Number of threads for parallel processing. Defaults to logical processor count.
*   **`--include <patterns>` (Optional):** Semicolon-separated glob patterns for files to include in the manifest.
*   **`--exclude <patterns>` (Optional):** Semicolon-separated glob patterns for files to exclude from the manifest.

---

### `add`

Scans a directory and adds new, unlisted files to an existing manifest.

```shell
Verity.exe add <manifestPath> [options]
```

#### Arguments

*   **`manifestPath` (Required):** The path to the existing manifest file to update.

#### Options

*   **`--root <directory>` (Optional):** The root directory to scan for new files. If omitted, uses manifest's directory.
*   **`--algorithm <name>` (Optional):** Hashing algorithm. If omitted, inferred from manifest extension. **Default: `SHA256`**.
*   **`--threads <number>` (Optional):** Number of threads for parallel processing. Defaults to logical processor count.
*   **`--include <patterns>` (Optional):** Semicolon-separated glob patterns for new files to include.
*   **`--exclude <patterns>` (Optional):** Semicolon-separated glob patterns for files to exclude from being added.

---

### Examples

**Basic Verification:**
Verify files listed in `C:\archive\manifest.sha256`. The files are expected to be in `C:\archive\`.

```shell
Verity.exe verify C:\archive\manifest.sha256
```

**Using a Different Root Directory:**
The manifest is in one location, but the data is in another.

```shell
Verity.exe verify C:\temp\manifest.sha256 --root D:\data\backups
```

**Creating a Manifest with Globs:**
Create a SHA256 manifest for the `D:\data\backups` directory, including JPG and PNG files, excluding TMP files.

```shell
Verity.exe create D:\data\backups\manifest.sha256 --root D:\data\backups --include "*.jpg;*.png" --exclude "*.tmp"
```

**Writing TSV Report to File:**
Verify files and write the TSV report to `errors.tsv`.

```shell
Verity.exe verify C:\archive\manifest.sha256 --tsvReport errors.tsv
```

**Adding New Files to a Manifest:**
Add new DOCX files from `D:\data\newfiles` to an existing manifest.

```shell
Verity.exe add D:\data\backups\manifest.sha256 --root D:\data\newfiles --include "*.docx"
```

---

## Output

### 1. Terminal UI (stdout)

During operation, Verity displays a live progress bar. Upon completion, it prints a summary and, if issues were found (or if `--showTable` is used), a detailed diagnostic table.

```
Verity v1.0.0 - Checksum Verifier

 ✓ Verifying files  100%

Verification Complete
  Success: 149
 Warnings: 2
    Errors: 1
Total Time: 12.34s
 Throughput: 810.12 MB/s

                            Diagnostic Report
┌────────┬──────────────────────────────────┬────────────────────────────────────────┬──────────┬──────────┐
│ Status │ File                             │ Details                                │ Expected │ Actual   │
├────────┼──────────────────────────────────┼────────────────────────────────────────┼──────────┼──────────┤
│ Error  │ D:\data\img\IMG_0101.JPG         │ Checksum mismatch.                     │ 820208…  │ d54084…  │
│ Warning│ D:\data\doc\report-final.docx    │ Checksum mismatch (file is newer).     │ 375d55…  │ 9c731e…  │
│ Warning│ D:\data\doc\untracked.txt        │ File exists but not in checksum list.  │ N/A      │ N/A      │
└────────┴──────────────────────────────────┴────────────────────────────────────────┴──────────┴──────────┘
```

### 2. Machine-Readable Report (TSV)

If any warnings or errors occur, a TSV report is sent to `stderr` (unless `--tsvReport` is specified). This stream can be redirected for logging or scripting.

```
#Status  File                            Details                                 ExpectedHash                            ActualHash
ERROR   D:\data\img\IMG_0101.JPG        Checksum mismatch.                      820208145359d1c620d459f00784e190        d540844afb8711b25448dc7589c25b5e
WARNING D:\data\doc\report-final.docx   Checksum mismatch (file is newer).      375d554729e87a93f65cd724bbd29d96        9c731e98fbb88d60e3501786d78684a1
WARNING D:\data\doc\untracked.txt       File exists but not in checksum list.
```

## Exit Codes

Verity uses process exit codes to signal the outcome of the operation.

| Code | Status   | Description                                                        |
| :--- | :------- | :----------------------------------------------------------------- |
| `0`  | Success  | All files were verified successfully. No warnings or errors.       |
| `1`  | Warning  | Verification completed, but one or more warnings were generated.   |
| `-1` | Error    | Verification completed, and one or more errors were generated.     |
| `-2` | Canceled | The operation was canceled by the user.                            |

You can check the exit code in scripts (e.g., `$LASTEXITCODE` in PowerShell, `%ERRORLEVEL%` in CMD).

---

## Building from Source

1.  Install the .NET 9 SDK.
2.  Clone this repository.
3.  Run the build script:

    ```powershell
    ./build.ps1
    ```

The native, self-contained executable will be located in `artifacts\Verity-v<version>.zip`.