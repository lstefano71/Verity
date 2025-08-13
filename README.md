# Verity

**Verity** is a high-performance, parallelized file integrity verification and manifest creation tool for modern Windows systems (.NET 9, C# 13.0). It is designed for speed, robust automation, and a rich terminal experience.

It is delivered as a single, self-contained executable with no external .NET runtime dependencies, built for speed and ease of use.

## Key Features

* **High Performance:** Concurrent producer-consumer architecture maximizes throughput, saturating CPU cores and I/O.
* **Manifest Creation & Update:** Generate checksum manifests or add new files to existing manifests.
* **Manifest Verification:** Verify file integrity against a manifest, with clear error and warning reporting.
* **Glob Filtering:** Powerful `--include` and `--exclude` glob patterns for file selection in all commands.
* **Intelligent I/O:** Dynamically adjusts file read buffer sizes for optimal performance on files of any size.
* **Rich Terminal UI:** Modern TUI powered by Spectre.Console, with live progress, summary, and diagnostics table.
* **Automation-Friendly:** Distinct exit codes for success, warning, error, and cancellation. Machine-readable TSV error/warning reports to file or `stderr`.
* **Robust Error Handling:** Differentiates between errors (e.g., hash mismatch) and warnings (e.g., newer file, unlisted files).
* **Resource Management:** Operates within reasonable memory limits; does not read all files into memory at once.
* **Self-Contained Executable:** Native AOT build for Windows, no external .NET runtime required.

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

### Common Options

All commands support these options:

* `--root <directory>`: Root directory for file operations. Defaults to manifest's directory.
* `--algorithm <name>`: Hashing algorithm (`SHA256`, `MD5`, `SHA1`). Inferred from manifest extension if omitted.
* `--threads <number>`: Number of parallel threads. Defaults to processor count.
* `--include <patterns>`: Semicolon-separated glob patterns for files to include (e.g., `*.jpg;*.docx`). Defaults to all files.
* `--exclude <patterns>`: Semicolon-separated glob patterns for files to exclude (e.g., `*.tmp;*.bak`). Defaults to none.
* `--showTable`: Force display of the diagnostic table even if there are no issues.
* `--tsvReport <file>`: Path to write a machine-readable TSV error/warning report. If omitted, TSV is written to `stderr` if issues are found.

### `verify`

Verifies the integrity of files based on a checksum manifest.

```
Verity.exe verify <checksumFile> [options]
```

* `<checksumFile>`: Path to the manifest file containing the checksums.

### `create`

Creates a checksum manifest from a directory.

```
Verity.exe create <outputManifest> [options]
```

* `<outputManifest>`: Path for the output manifest file.

### `add`

Scans a directory and adds new, unlisted files to an existing manifest.

```
Verity.exe add <manifestPath> [options]
```

* `<manifestPath>`: Path to the existing manifest file to update.

---

## Glob Patterns

- Multiple patterns separated by semicolons (`;`).
- `--include` specifies files to include; `--exclude` specifies files to exclude.
- Patterns like `*.txt` match files at all levels below the root.
- Directory globs supported, e.g., `docs/**/*.md` matches all Markdown files in any descendant directory of `docs`.
- If omitted, all files are included and none are excluded.

---

## Output & Exit Codes

- **Terminal UI:** Live progress, summary, and diagnostics table.
- **TSV Report:** Machine-readable error/warning report to file or `stderr`.
- **Exit Codes:**
  - `0`: Success (no warnings/errors)
  - `1`: Warning (warnings, no errors)
  - `-1`: Error (errors found)
  - `-2`: Canceled by user

---

## Examples

**Basic Verification:**
```
Verity.exe verify C:\archive\manifest.sha256
```

**Using a Different Root Directory:**
```
Verity.exe verify C:\temp\manifest.sha256 --root D:\data\backups
```

**Creating a Manifest with Globs:**
```
Verity.exe create D:\data\backups\manifest.sha256 --root D:\data\backups --include "*.jpg;*.png" --exclude "*.tmp"
```

**Writing TSV Report to File:**
```
Verity.exe verify C:\archive\manifest.sha256 --tsvReport errors.tsv
```

**Adding New Files to a Manifest:**
```
Verity.exe add D:\data\backups\manifest.sha256 --root D:\data\newfiles --include "*.docx"
```

---

## Building from Source

1. Install the .NET 9 SDK.
2. Clone this repository.
3. Run the build script:

    ```powershell
    ./build.ps1
    ```

The native, self-contained executable will be located in `artifacts\Verity-v<version>.zip`.