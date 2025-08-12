# Feature Request a01: glob filters

## Overview
This feature request is about adding glob filters to the `verify`, `create`, and `add` commands in Verity. This will allow users to specify patterns for files they want to include or exclude when generating or verifying checksums.

## Proposed Changes
### `verify`
Add `--include` and `--exclude`options to the `verify` command to filter files based on glob patterns.

```shell
Verity.exe verify <checksumFile> --include "*.txt;*.md" --exclude "*.tmp" [options]
```

### 'create' and 'add'
Add similar `--include` and `--exclude` options to the `create` and `add` commands.

```shell
Verity.exe create <outputManifest> --include "*.txt;*.md" --exclude "*.tmp" [options]
Verity.exe add <manifestPath> --include "*.txt;*.md" --exclude "*.tmp" [options]
```

## Glob patterns
Multiple patterns can be separated by semicolons (`;`). The `--include` option specifies which files to include, while the `--exclude` option specifies which files to exclude from the operation.

Patterns of the form `"*.txt"` or `"*.md"` will match files with those extensions at all levels below the root.

Patterns can also include directory names, such as `"docs/*.md"` to match Markdown files in the `docs` directory. Directory are always specified relative to the root directory. Directories can themselves be globs, such as `"docs/*/README.md"` to match README files in any subdirectory of `docs` or `"docs/**/*.md"` to match all Markdown files in any descendant directory of `docs`.

## Default Behavior
By default, if no `--include` or `--exclude` options are specified, all files in the root directory and its subdirectories will be included in the operation. A missing `--include` option will default to `*`, meaning all files are included, while a missing `--exclude` option will default to an empty set, meaning no files are excluded.

## User Experience
Users will be able to see active globs in the intro panel along with the pre-existing information such as the root, the version number and so on.

## Implementation suggestions
- If there are .NET libraries which already implement glob matching in a way that is compatible with the requirements, they should be used instead of reinventing the wheel.
- Write reusable code for the normalization of globs, so that the same code can be used in all commands.
- Write reusable code for matching files against the globs, so that the same code can be used in all commands.
- Possibly refactor the commands `add` and `create` to use `CliOptions`, so that the glob options can be passed in a consistent way.