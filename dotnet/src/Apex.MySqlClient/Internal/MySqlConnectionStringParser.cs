/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Text;

namespace Apex.MySqlClient.Internal;

/// <summary>Parses the keyword and URI query forms of a MySQL connection string.</summary>
internal static class MySqlConnectionStringParser
{
  /// <summary>Parses a semicolon separated <c>key=value</c> connection string.</summary>
  internal static IReadOnlyDictionary<string, string> ParseKeywords(string connectionString)
  {
    Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    ReadOnlySpan<char> input = connectionString.AsSpan();
    int position = 0;
    while (position < input.Length)
    {
      SkipWhitespace(input, ref position);
      while (position < input.Length && input[position] == ';')
      {
        position++;
        SkipWhitespace(input, ref position);
      }

      if (position == input.Length)
      {
        break;
      }

      int keyStart = position;
      while (position < input.Length && input[position] != '=')
      {
        position++;
      }

      if (position == input.Length)
      {
        throw new FormatException(
          $"MySQL connection-string key '{input[keyStart..].Trim().ToString()}' has no value.");
      }

      string key = input[keyStart..position].Trim().ToString();
      if (key.Length == 0)
      {
        throw new FormatException("MySQL connection-string key is empty.");
      }

      position++;
      SkipWhitespace(input, ref position);
      string value = position < input.Length && input[position] is '\'' or '"'
        ? ParseQuoted(input, ref position)
        : ParseUnquoted(input, ref position);
      values[key] = value;
    }

    return values;
  }

  /// <summary>Parses the query component of a <c>mysql://</c> or <c>mariadb://</c> URI.</summary>
  internal static IReadOnlyDictionary<string, string> ParseQuery(string query)
  {
    Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
      int separator = part.IndexOf('=', StringComparison.Ordinal);
      string key = separator < 0 ? part : part[..separator];
      string value = separator < 0 ? string.Empty : part[(separator + 1)..];
      values[Decode(key)] = Decode(value);
    }

    return values;
  }

  private static string ParseQuoted(ReadOnlySpan<char> input, ref int position)
  {
    char quote = input[position++];
    StringBuilder value = new();
    while (position < input.Length)
    {
      char current = input[position++];
      if (current == quote)
      {
        if (position < input.Length && input[position] == quote)
        {
          position++;
          value.Append(quote);
          continue;
        }

        SkipWhitespace(input, ref position);
        if (position < input.Length && input[position] != ';')
        {
          throw new FormatException(
            "MySQL quoted connection-string value must be followed by a semicolon.");
        }

        if (position < input.Length)
        {
          position++;
        }

        return value.ToString();
      }

      value.Append(current);
    }

    throw new FormatException("MySQL quoted connection-string value is unterminated.");
  }

  private static string ParseUnquoted(ReadOnlySpan<char> input, ref int position)
  {
    int start = position;
    while (position < input.Length && input[position] != ';')
    {
      position++;
    }

    ReadOnlySpan<char> value = input[start..position];
    if (position < input.Length)
    {
      position++;
    }

    return value.TrimEnd().ToString();
  }

  private static void SkipWhitespace(ReadOnlySpan<char> input, ref int position)
  {
    while (position < input.Length && char.IsWhiteSpace(input[position]))
    {
      position++;
    }
  }

  private static string Decode(string value) =>
    Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
}
