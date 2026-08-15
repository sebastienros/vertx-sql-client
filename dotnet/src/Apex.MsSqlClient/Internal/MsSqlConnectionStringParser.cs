/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

namespace Apex.MsSqlClient.Internal;

internal static class MsSqlConnectionStringParser
{
  internal static IReadOnlyDictionary<string, string> Parse(string connectionString)
  {
    Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    int position = 0;
    while (position < connectionString.Length)
    {
      SkipWhitespaceAndSeparators(connectionString, ref position);
      if (position == connectionString.Length)
      {
        break;
      }

      int equals = connectionString.IndexOf('=', position);
      if (equals < 0)
      {
        throw new FormatException("SQL Server connection options must use key=value syntax.");
      }

      string key = connectionString[position..equals].Trim();
      if (key.Length == 0)
      {
        throw new FormatException("SQL Server connection option name cannot be empty.");
      }

      position = equals + 1;
      string value = ReadValue(connectionString, ref position);
      values[key] = value;
    }

    return values;
  }

  internal static IReadOnlyDictionary<string, string> ParseQuery(string query)
  {
    Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    ReadOnlySpan<char> text = query.AsSpan().TrimStart('?');
    foreach (Range range in text.Split('&'))
    {
      ReadOnlySpan<char> pair = text[range];
      if (pair.IsEmpty)
      {
        continue;
      }

      int equals = pair.IndexOf('=');
      string key = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).ToString());
      string value = equals < 0
        ? string.Empty
        : Uri.UnescapeDataString(pair[(equals + 1)..].ToString());
      values[key] = value;
    }

    return values;
  }

  private static string ReadValue(string text, ref int position)
  {
    while (position < text.Length && char.IsWhiteSpace(text[position]))
    {
      position++;
    }

    if (position == text.Length)
    {
      return string.Empty;
    }

    char quote = text[position];
    if (quote is '\'' or '"' or '{')
    {
      position++;
      char terminator = quote == '{' ? '}' : quote;
      System.Text.StringBuilder value = new();
      while (position < text.Length)
      {
        char current = text[position++];
        if (current == terminator)
        {
          if (position < text.Length && text[position] == terminator)
          {
            value.Append(terminator);
            position++;
            continue;
          }

          while (position < text.Length && char.IsWhiteSpace(text[position]))
          {
            position++;
          }

          if (position < text.Length && text[position] != ';')
          {
            throw new FormatException("Unexpected characters after quoted SQL Server option.");
          }

          return value.ToString();
        }

        value.Append(current);
      }

      throw new FormatException("Unterminated quoted SQL Server connection option.");
    }

    int end = text.IndexOf(';', position);
    if (end < 0)
    {
      end = text.Length;
    }

    string result = text[position..end].Trim();
    position = end;
    return result;
  }

  private static void SkipWhitespaceAndSeparators(string text, ref int position)
  {
    while (position < text.Length &&
           (char.IsWhiteSpace(text[position]) || text[position] == ';'))
    {
      position++;
    }
  }
}
