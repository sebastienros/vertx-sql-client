/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

namespace Apex.SqlClient.Internal;

internal static class ConnectionStringQueryParser
{
  internal static IReadOnlyDictionary<string, string> Parse(string query)
  {
    Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    foreach (string part in query.TrimStart('?').Split(
               '&',
               StringSplitOptions.RemoveEmptyEntries))
    {
      int separator = part.IndexOf('=');
      string key = separator < 0 ? part : part[..separator];
      string value = separator < 0 ? string.Empty : part[(separator + 1)..];
      values[Decode(key)] = Decode(value);
    }

    return values;
  }

  private static string Decode(string value) =>
    Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
}
