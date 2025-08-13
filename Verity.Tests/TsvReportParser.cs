public record TsvReportRow(string Status, string File, string Details, string ExpectedHash, string ActualHash);

public static class TsvReportParser
{
  public static List<TsvReportRow> Parse(string tsvContent)
  {
    var rows = new List<TsvReportRow>();
    var lines = tsvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    var dataLines = lines.Where(l => !l.StartsWith("#"));
    foreach (var line in dataLines) {
      var parts = line.Split('\t');
      if (parts.Length == 5) {
        rows.Add(new TsvReportRow(
            Status: parts[0],
            File: parts[1],
            Details: parts[2],
            ExpectedHash: parts[3],
            ActualHash: parts[4]
        ));
      }
    }
    return rows;
  }
}
