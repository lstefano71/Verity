string ComputeHash<T>(string filePath, Func<T> factory) 
    where T : System.Security.Cryptography.HashAlgorithm
{
   using var hasher = factory();
   using var stream = System.IO.File.OpenRead(filePath);
   byte[] hashBytes = hasher.ComputeHash(stream);
   return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
}

void ValidateManifestStrict(string manifestPath, string testRoot, string algoName)
{
   foreach (var line in System.IO.File.ReadLines(manifestPath))
   {
      if (string.IsNullOrWhiteSpace(line) || !line.Contains('\t'))
      {
         Error($"[FAIL] Manifest format error for {algoName}: '{line}'");
         continue;
      }

      var parts = line.Split('\t');
      if (parts is not [var expectedHash, var relPath])
      {
         Error($"[FAIL] Manifest format error for {algoName}: '{line}'");
         continue;
      }

      var filePath = System.IO.Path.Combine(testRoot, relPath);
      if (!System.IO.File.Exists(filePath))
      {
         Error($"[FAIL] Manifest references missing file for {algoName}: '{relPath}'");
         continue;
      }

      string actualHash = algoName switch
      {
         "SHA256" => ComputeHash(filePath, System.Security.Cryptography.SHA256.Create),
         "MD5"    => ComputeHash(filePath, System.Security.Cryptography.MD5.Create),
         "SHA1"   => ComputeHash(filePath, System.Security.Cryptography.SHA1.Create),
         _        => throw new InvalidOperationException($"Unknown algorithm: {algoName}")
      };

      if (actualHash != expectedHash)
      {
         Error($"[FAIL] Hash mismatch for {algoName}: '{relPath}' expected {expectedHash} actual {actualHash}");
      }
   }
   Information($"Manifest strict validation completed for {algoName}.");
}
