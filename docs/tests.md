# Test suite in Cake for Verity

This document outlines the test suite for the Verity project using Cake, a cross-platform build automation system with a C# DSL.

## Test Steps
1. Create a file system starting from a random root directory.
2. Fill the file system with a set of files and directories.
3. Execute Verity create to generate the manifest.
4. Execute Verity verify to verify the manifest
5. Introduce explicit errors in the manifest:
    - Remove a file from the file system.
    - Modify a file in the file system.
    - Add a new file to the file system.
6. Execute Verity verify again to check for errors.
