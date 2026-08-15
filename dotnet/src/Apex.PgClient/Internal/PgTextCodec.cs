/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Apex.SqlClient;

namespace Apex.PgClient.Internal;

internal static class PgTextCodec
{
    private static readonly Encoding s_utf8 = new UTF8Encoding(false, true);

    public static object Decode(uint typeId, ReadOnlyMemory<byte> value)
    {
        var text = s_utf8.GetString(value.Span);
        var elementType = GetArrayElementType(typeId);
        if (elementType != 0)
        {
            return DecodeArray(elementType, text);
        }

        return DecodeText(typeId, text, value);
    }

    internal static bool DecodeBoolean(ReadOnlySpan<byte> value)
    {
        if (value.SequenceEqual("t"u8))
        {
            return true;
        }

        if (value.SequenceEqual("f"u8))
        {
            return false;
        }

        throw new FormatException("Invalid PostgreSQL BOOL value.");
    }

    internal static byte[] DecodeBytes(ReadOnlySpan<byte> value) =>
      ParseBytea(s_utf8.GetString(value));

    internal static short DecodeInt16(ReadOnlySpan<byte> value) =>
      Utf8Parser.TryParse(value, out short parsed, out var consumed) &&
      consumed == value.Length
        ? parsed
        : throw new FormatException("Invalid PostgreSQL INT2 value.");

    internal static int DecodeInt32(ReadOnlySpan<byte> value) =>
      Utf8Parser.TryParse(value, out int parsed, out var consumed) &&
      consumed == value.Length
        ? parsed
        : throw new FormatException("Invalid PostgreSQL INT4 value.");

    internal static long DecodeInt64(ReadOnlySpan<byte> value) =>
      Utf8Parser.TryParse(value, out long parsed, out var consumed) &&
      consumed == value.Length
        ? parsed
        : throw new FormatException("Invalid PostgreSQL INT8 value.");

    internal static float DecodeFloat(ReadOnlySpan<byte> value) =>
      Utf8Parser.TryParse(value, out float parsed, out var consumed) &&
      consumed == value.Length
        ? parsed
        : throw new FormatException("Invalid PostgreSQL FLOAT4 value.");

    internal static double DecodeDouble(ReadOnlySpan<byte> value) =>
      Utf8Parser.TryParse(value, out double parsed, out var consumed) &&
      consumed == value.Length
        ? parsed
        : throw new FormatException("Invalid PostgreSQL FLOAT8 value.");

    internal static decimal DecodeDecimal(ReadOnlySpan<byte> value) =>
      Utf8Parser.TryParse(value, out decimal parsed, out var consumed) &&
      consumed == value.Length
        ? parsed
        : throw new FormatException("Invalid PostgreSQL NUMERIC value.");

    internal static string DecodeString(ReadOnlySpan<byte> value) =>
      s_utf8.GetString(value);

    internal static Guid DecodeGuid(ReadOnlySpan<byte> value) =>
      Utf8Parser.TryParse(value, out Guid parsed, out var consumed) &&
      consumed == value.Length
        ? parsed
        : throw new FormatException("Invalid PostgreSQL UUID value.");

    internal static DateOnly DecodeDateOnly(ReadOnlySpan<byte> value) =>
      ParseDate(s_utf8.GetString(value));

    internal static TimeOnly DecodeTimeOnly(ReadOnlySpan<byte> value) =>
      TimeOnly.Parse(s_utf8.GetString(value), CultureInfo.InvariantCulture);

    internal static DateTime DecodeDateTime(ReadOnlySpan<byte> value) =>
      ParseTimestamp(s_utf8.GetString(value));

    internal static DateTimeOffset DecodeDateTimeOffset(
        ReadOnlySpan<byte> value) =>
      ParseTimestampWithTimeZone(s_utf8.GetString(value));

    internal static JsonElement DecodeJson(ReadOnlyMemory<byte> value) =>
      ParseJson(value);

    internal static PgNumeric DecodeNumeric(ReadOnlySpan<byte> value) =>
      PgNumeric.Parse(s_utf8.GetString(value));

    internal static PgMoney DecodeMoney(ReadOnlySpan<byte> value) =>
      ParseMoney(s_utf8.GetString(value));

    internal static PgInterval DecodeInterval(ReadOnlySpan<byte> value) =>
      ParseInterval(s_utf8.GetString(value));

    internal static PgTimeWithTimeZone DecodeTimeWithTimeZone(
        ReadOnlySpan<byte> value) =>
      ParseTimeWithTimeZone(s_utf8.GetString(value));

    internal static PgPoint DecodePoint(ReadOnlySpan<byte> value) =>
      ParsePoint(s_utf8.GetString(value));

    internal static PgLineSegment DecodeLineSegment(
        ReadOnlySpan<byte> value) =>
      ParseLineSegment(s_utf8.GetString(value));

    internal static PgPath DecodePath(ReadOnlySpan<byte> value) =>
      ParsePath(s_utf8.GetString(value));

    internal static PgBox DecodeBox(ReadOnlySpan<byte> value) =>
      ParseBox(s_utf8.GetString(value));

    internal static PgPolygon DecodePolygon(ReadOnlySpan<byte> value) =>
      ParsePolygon(s_utf8.GetString(value));

    internal static PgLine DecodeLine(ReadOnlySpan<byte> value) =>
      ParseLine(s_utf8.GetString(value));

    internal static PgCidr DecodeCidr(ReadOnlySpan<byte> value) =>
      ParseCidr(s_utf8.GetString(value));

    internal static PgCircle DecodeCircle(ReadOnlySpan<byte> value) =>
      ParseCircle(s_utf8.GetString(value));

    internal static PgInet DecodeInet(ReadOnlySpan<byte> value) =>
      ParseInet(s_utf8.GetString(value));

    internal static object?[] DecodeArray(
        uint typeId,
        ReadOnlyMemory<byte> value)
    {
        var elementType = GetArrayElementType(typeId);
        if (elementType == 0)
        {
            throw new PgUnsupportedTypeException(typeId);
        }

        return DecodeArray(elementType, s_utf8.GetString(value.Span));
    }

    private static object DecodeText(
        uint typeId,
        string text,
        ReadOnlyMemory<byte> utf8 = default) =>
      typeId switch
      {
          16 => text == "t",
          17 => ParseBytea(text),
          20 => long.Parse(
          text,
          NumberStyles.Integer,
          CultureInfo.InvariantCulture),
          21 => short.Parse(
          text,
          NumberStyles.Integer,
          CultureInfo.InvariantCulture),
          23 => int.Parse(
          text,
          NumberStyles.Integer,
          CultureInfo.InvariantCulture),
          26 or 142 or 829 or 1560 or 1562 or 2278 or 774 =>
          throw new PgUnsupportedTypeException(typeId),
          700 => float.Parse(
          text,
          NumberStyles.Float,
          CultureInfo.InvariantCulture),
          701 => double.Parse(
          text,
          NumberStyles.Float,
          CultureInfo.InvariantCulture),
          1700 => PgNumeric.Parse(text),
          1082 => ParseDate(text),
          1083 => TimeOnly.Parse(text, CultureInfo.InvariantCulture),
          1266 => ParseTimeWithTimeZone(text),
          1114 => ParseTimestamp(text),
          1184 => ParseTimestampWithTimeZone(text),
          1186 => ParseInterval(text),
          2950 => Guid.Parse(text),
          114 or 3802 => utf8.IsEmpty
          ? ParseJson(text)
          : ParseJson(utf8),
          600 => ParsePoint(text),
          601 => ParseLineSegment(text),
          602 => ParsePath(text),
          603 => ParseBox(text),
          604 => ParsePolygon(text),
          628 => ParseLine(text),
          650 => ParseCidr(text),
          718 => ParseCircle(text),
          790 => ParseMoney(text),
          869 => ParseInet(text),
          _ => text,
      };

    private static uint GetArrayElementType(uint typeId) =>
      typeId switch
      {
          1000 => 16,
          1001 => 17,
          1002 => 18,
          1003 => 19,
          1005 => 21,
          1007 => 23,
          1009 => 25,
          1015 => 1043,
          1016 => 20,
          1017 => 600,
          1018 => 601,
          1019 => 602,
          1020 => 603,
          1021 => 700,
          1022 => 701,
          1027 => 604,
          1041 => 869,
          1115 => 1114,
          1182 => 1082,
          1183 => 1083,
          1185 => 1184,
          1187 => 1186,
          1231 => 1700,
          1270 => 1266,
          199 => 114,
          629 => 628,
          651 => 650,
          719 => 718,
          791 => 790,
          2951 => 2950,
          3807 => 3802,
          _ => 0,
      };

    private static object?[] DecodeArray(uint elementType, string text) =>
      PgArrayParser.Parse(text, elementType == 603 ? ';' : ',')
        .Select(item => item is null ? null : DecodeText(elementType, item))
        .ToArray();

    private static byte[] ParseBytea(string text) =>
      text.StartsWith("\\x", StringComparison.Ordinal)
        ? Convert.FromHexString(text.AsSpan(2))
        : throw new NotSupportedException(
          "Only PostgreSQL hex bytea output is supported.");

    private static JsonElement ParseJson(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static JsonElement ParseJson(ReadOnlyMemory<byte> utf8)
    {
        using JsonDocument document = JsonDocument.Parse(utf8);
        return document.RootElement.Clone();
    }

    private static DateOnly ParseDate(string text) =>
      text switch
      {
          "infinity" => DateOnly.MaxValue,
          "-infinity" => DateOnly.MinValue,
          _ => DateOnly.ParseExact(
          text,
          "yyyy-MM-dd",
          CultureInfo.InvariantCulture),
      };

    private static DateTime ParseTimestamp(string text) =>
      text switch
      {
          "infinity" => DateTime.MaxValue,
          "-infinity" => DateTime.MinValue,
          _ => DateTime.SpecifyKind(
          DateTime.Parse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces),
          DateTimeKind.Unspecified),
      };

    private static DateTimeOffset ParseTimestampWithTimeZone(string text) =>
      text switch
      {
          "infinity" => DateTimeOffset.MaxValue,
          "-infinity" => DateTimeOffset.MinValue,
          _ => DateTimeOffset.Parse(
          text,
          CultureInfo.InvariantCulture,
          DateTimeStyles.AllowWhiteSpaces),
      };

    private static PgTimeWithTimeZone ParseTimeWithTimeZone(string text)
    {
        var separator = text.LastIndexOfAny('+', '-');
        if (separator <= 0)
        {
            throw new FormatException(
              "Invalid PostgreSQL time with time zone.");
        }

        TimeOnly time = TimeOnly.Parse(
          text[..separator],
          CultureInfo.InvariantCulture);
        var offsetText = text.AsSpan(separator);
        var sign = offsetText[0] == '-' ? -1 : 1;
        var parts = offsetText[1..].ToString().Split(':');
        var hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minutes = parts.Length > 1
          ? int.Parse(parts[1], CultureInfo.InvariantCulture)
          : 0;
        var seconds = parts.Length > 2
          ? int.Parse(parts[2], CultureInfo.InvariantCulture)
          : 0;
        TimeSpan offset = new(
          0,
          sign * hours,
          sign * minutes,
          sign * seconds);
        return new PgTimeWithTimeZone(time, offset);
    }

    private static PgInterval ParseInterval(string text)
    {
        var match = Regex.Match(
          text,
          @"^(?<sign>-)?P(?:(?<years>[+-]?\d+)Y)?(?:(?<months>[+-]?\d+)M)?(?:(?<days>[+-]?\d+)D)?" +
          @"(?:T(?:(?<hours>[+-]?\d+)H)?(?:(?<minutes>[+-]?\d+)M)?(?:(?<seconds>[+-]?\d+)(?:\.(?<fraction>\d{1,6}))?S)?)?$",
          RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new FormatException(
              $"Invalid PostgreSQL ISO-8601 interval '{text}'.");
        }

        var sign = match.Groups["sign"].Success ? -1 : 1;
        var fraction = ParseGroup(match, "fraction");
        if (match.Groups["fraction"].Success)
        {
            fraction *= (int)Math.Pow(
              10,
              6 - match.Groups["fraction"].Length);
        }

        var fractionSign =
          match.Groups["seconds"].Success &&
          match.Groups["seconds"].Value.StartsWith(
            "-",
            StringComparison.Ordinal)
            ? -1
            : 1;

        return new PgInterval(
          sign * ParseGroup(match, "years"),
          sign * ParseGroup(match, "months"),
          sign * ParseGroup(match, "days"),
          sign * ParseGroup(match, "hours"),
          sign * ParseGroup(match, "minutes"),
          sign * ParseGroup(match, "seconds"),
          sign * fractionSign * fraction);
    }

    private static int ParseGroup(Match match, string name) =>
      match.Groups[name].Success
        ? int.Parse(
          match.Groups[name].Value,
          CultureInfo.InvariantCulture)
        : 0;

    private static PgPoint ParsePoint(string text)
    {
        var values = ParseDoubles(text);
        return values.Length == 2
          ? new PgPoint(values[0], values[1])
          : throw new FormatException("Invalid PostgreSQL point.");
    }

    private static PgLine ParseLine(string text)
    {
        var values = ParseDoubles(text);
        return values.Length == 3
          ? new PgLine(values[0], values[1], values[2])
          : throw new FormatException("Invalid PostgreSQL line.");
    }

    private static PgLineSegment ParseLineSegment(string text)
    {
        var values = ParseDoubles(text);
        return values.Length == 4
          ? new PgLineSegment(
            new PgPoint(values[0], values[1]),
            new PgPoint(values[2], values[3]))
          : throw new FormatException(
            "Invalid PostgreSQL line segment.");
    }

    private static PgBox ParseBox(string text)
    {
        var values = ParseDoubles(text);
        return values.Length == 4
          ? new PgBox(
            new PgPoint(values[0], values[1]),
            new PgPoint(values[2], values[3]))
          : throw new FormatException("Invalid PostgreSQL box.");
    }

    private static PgPath ParsePath(string text) =>
      new(ParsePoints(text), text.Length > 0 && text[0] == '(');

    private static PgPolygon ParsePolygon(string text) =>
      new(ParsePoints(text));

    private static PgCircle ParseCircle(string text)
    {
        var values = ParseDoubles(text);
        return values.Length == 3
          ? new PgCircle(
            new PgPoint(values[0], values[1]),
            values[2])
          : throw new FormatException("Invalid PostgreSQL circle.");
    }

    private static PgPoint[] ParsePoints(string text)
    {
        var values = ParseDoubles(text);
        if (values.Length == 0 || values.Length % 2 != 0)
        {
            throw new FormatException(
              "Invalid PostgreSQL point collection.");
        }

        PgPoint[] points = new PgPoint[values.Length / 2];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new PgPoint(
              values[i * 2],
              values[(i * 2) + 1]);
        }

        return points;
    }

    private static double[] ParseDoubles(string text) =>
      Regex.Matches(
          text,
          @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
          RegexOptions.CultureInvariant)
        .Select(match => double.Parse(
          match.Value,
          NumberStyles.Float,
          CultureInfo.InvariantCulture))
        .ToArray();

    private static PgInet ParseInet(string text)
    {
        (var address, var prefix) = ParseNetwork(text);
        return new PgInet(address, prefix);
    }

    private static PgCidr ParseCidr(string text)
    {
        (var address, var prefix) = ParseNetwork(text);
        var requiredPrefix = prefix ??
          (address.AddressFamily ==
           System.Net.Sockets.AddressFamily.InterNetwork
            ? 32
            : 128);
        return new PgCidr(address, requiredPrefix);
    }

    private static (
      IPAddress Address,
      int? Prefix) ParseNetwork(string text)
    {
        var separator = text.LastIndexOf('/');
        return separator < 0
          ? (IPAddress.Parse(text), null)
          : (
            IPAddress.Parse(text[..separator]),
            int.Parse(
              text[(separator + 1)..],
              CultureInfo.InvariantCulture));
    }

    private static PgMoney ParseMoney(string text)
    {
        string normalized = new(
          text.Where(character =>
              char.IsDigit(character) ||
              character is '-' or '+' or '.')
            .ToArray());
        return new PgMoney(
          decimal.Parse(
            normalized,
            CultureInfo.InvariantCulture));
    }

    public static string FormatParameter(SqlValue value) =>
      value.Kind switch
      {
          SqlValueKind.Null =>
          throw new InvalidOperationException(
            "NULL parameters have no text payload."),
          SqlValueKind.Boolean =>
          value.Get<bool>() ? "true" : "false",
          SqlValueKind.Bytes =>
          "\\x" + Convert.ToHexStringLower(
            value.GetRequired<byte[]>()),
          SqlValueKind.ReadOnlyMemory =>
          "\\x" + Convert.ToHexStringLower(
            value.Get<ReadOnlyMemory<byte>>().Span),
          SqlValueKind.DateOnly =>
          value.Get<DateOnly>().ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture),
          SqlValueKind.TimeOnly =>
          value.Get<TimeOnly>().ToString(
            "HH:mm:ss.fffffff",
            CultureInfo.InvariantCulture),
          SqlValueKind.DateTime =>
          value.Get<DateTime>().ToString(
            "yyyy-MM-dd HH:mm:ss.fffffff",
            CultureInfo.InvariantCulture),
          SqlValueKind.DateTimeOffset =>
          value.Get<DateTimeOffset>().ToString(
            "yyyy-MM-dd HH:mm:ss.fffffffzzz",
            CultureInfo.InvariantCulture),
          SqlValueKind.Guid => value.Get<Guid>().ToString("D"),
          SqlValueKind.JsonDocument =>
          value.GetRequired<JsonDocument>().RootElement.GetRawText(),
          SqlValueKind.JsonElement =>
          value.Get<JsonElement>().GetRawText(),
          _ => value.ToObject() is IFormattable formattable
          ? formattable.ToString(
            null,
            CultureInfo.InvariantCulture)
          : value.ToObject()?.ToString() ??
            throw new InvalidOperationException(
              "Parameter has no text representation."),
      };
}
