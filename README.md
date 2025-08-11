# Verity

**Verity** is a high-performance, parallelized file integrity verification tool for modern Windows systems. It is designed to be extremely fast, provide excellent real-time feedback, and be easily integrated into automated scripts.

It is delivered as a single, self-contained executable with no external .NET runtime dependencies, built for speed and ease of use.

## Key Features

* **High Performance:** Utilizes a concurrent producer-consumer architecture to maximize throughput by keeping CPU cores and the I/O subsystem saturated.
* **Intelligent I/O:** Automatically adjusts file read buffer sizes based on file size to optimize performance for both very large and very small files.
* **Rich TUI:** A clean and modern Terminal User Interface powered by Spectre.Console provides live progress, throughput metrics, and a detailed final report.
* **Automation-Friendly:** Provides distinct exit codes for success, warning, and error states. It also generates a machine-readable TSV (Tab-Separated Values) report on `stderr` for easy parsing by other tools.
* **Robust Error Handling:** Differentiates between critical errors (e.g., hash mismatch) and warnings (e.g., mismatch on a newer file, unlisted files), giving the user a clear picture of their data's state.

---

## Usage

Verity is a command-line tool. The basic syntax is:

```shell
Verity.exe <checksumFile> [options]
```

### Arguments

* **`checksumFile` (Required):** The path to the manifest file containing the checksums. The format must be one entry per line: `hash<tab>relative_path`.

### Options

* **`--root <directory>` (Optional):** The root directory for the files. If omitted, Verity uses the directory where the `checksumFile` is located.
* **`--algorithm <name>` (Optional):** The hashing algorithm to use. This must be a name recognized by the .NET cryptography services. **Default: `SHA256`**.

### Examples

**Basic Verification:**
Verify files listed in `C:\archive\manifest.sha256`. The files are expected to be in `C:\archive\`.

```shell
Verity.exe C:\archive\manifest.sha256
```

**Using a Different Root Directory:**
The manifest is in one location, but the data is in another.

```shell
Verity.exe C:\temp\manifest.sha256 --root D:\data\backups
```

**Using a Different Algorithm:**
Verify files using the MD5 algorithm.

```shell
Verity.exe files.md5 --algorithm MD5
```

---

## Output

### 1. Terminal UI (stdout)

During operation, Verity displays a live progress bar. Upon completion, it prints a summary and, if issues were found, a detailed diagnostic table.

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

### 2. Machine-Readable Report (stderr)

If any warnings or errors occur, a TSV report is sent to `stderr`. This stream can be redirected for logging or scripting.

```shell
#Status  File                            Details                                 ExpectedHash                            ActualHash
ERROR   D:\data\img\IMG_0101.JPG        Checksum mismatch.                      820208145359d1c620d459f00784e190        d540844afb8711b25448dc7589c25b5e
WARNING D:\data\doc\report-final.docx   Checksum mismatch (file is newer).      375d554729e87a93f65cd724bbd29d96        9c731e98fbb88d60e3501786d78684a1
WARNING D:\data\doc\untracked.txt       File exists but not in checksum list.
```

## Exit Codes

Verity uses process exit codes to signal the outcome of the verification.

| Code | Status   | Description                                                        |
| :--- | :------- | :----------------------------------------------------------------- |
| `0`  | Success  | All files were verified successfully. No warnings or errors.       |
| `1`  | Warning  | Verification completed, but one or more warnings were generated.   |
| `-1` | Error    | Verification completed, and one or more errors were generated.     |

You can check the exit code in scripts (e.g., `$LASTEXITCODE` in PowerShell, `%ERRORLEVEL%` in CMD).

---

## Building from Source

1. Install the .NET 9 SDK.
2. Clone this repository.
3. Run `dotnet publish -c Release` from the project's root directory.

The native, self-contained executable will be located in `bin\Release\net9.0\win-x64\publish\Verity.exe`.
