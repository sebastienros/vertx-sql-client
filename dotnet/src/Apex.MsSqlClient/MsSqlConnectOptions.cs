/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Apex.SqlClient;
using Apex.MsSqlClient.Internal;

namespace Apex.MsSqlClient;

public sealed record MsSqlConnectOptions : SqlConnectOptions
{
  public MsSqlConnectOptions()
  {
    Port = 1433;
    Username = "sa";
    Database = string.Empty;
  }

  public MsSqlEncryptionMode EncryptionMode { get; init; } = MsSqlEncryptionMode.Require;

  public bool TrustServerCertificate { get; init; }

  public string? TlsHostName { get; init; }

  public RemoteCertificateValidationCallback? CertificateValidationCallback { get; init; }

  public IReadOnlyList<X509Certificate2> ClientCertificates { get; init; } =
    Array.Empty<X509Certificate2>();

  public X509RevocationMode CertificateRevocationCheckMode { get; init; } =
    X509RevocationMode.NoCheck;

  public string ApplicationName { get; init; } = "apex-mssql-client";

  public string ClientInterfaceName { get; init; } = "Apex.MsSqlClient";

  public string? WorkstationId { get; init; }

  public int PacketSize { get; init; } = 4096;

  public int StringCacheCapacity { get; init; } = 1024;

  public int StringCacheMaximumByteLength { get; init; } = 128;

  public static MsSqlConnectOptions Parse(string connectionString)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
    return connectionString.StartsWith("sqlserver://", StringComparison.OrdinalIgnoreCase)
      ? ParseUri(connectionString)
      : Apply(new MsSqlConnectOptions(), MsSqlConnectionStringParser.Parse(connectionString));
  }

  private static MsSqlConnectOptions ParseUri(string connectionString)
  {
    if (!Uri.TryCreate(connectionString, UriKind.Absolute, out Uri? uri) ||
        !string.Equals(uri.Scheme, "sqlserver", StringComparison.OrdinalIgnoreCase))
    {
      throw new FormatException("Invalid SQL Server connection URI.");
    }

    MsSqlConnectOptions options = new()
    {
      Host = uri.Host,
      Port = uri.IsDefaultPort ? 1433 : uri.Port,
      Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
    };
    if (uri.UserInfo.Length > 0)
    {
      int separator = uri.UserInfo.IndexOf(':');
      options = options with
      {
        Username = Uri.UnescapeDataString(
          separator < 0 ? uri.UserInfo : uri.UserInfo[..separator]),
        Password = separator < 0
          ? string.Empty
          : Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..]),
      };
    }

    return Apply(options, MsSqlConnectionStringParser.ParseQuery(uri.Query));
  }

  private static MsSqlConnectOptions Apply(
    MsSqlConnectOptions options,
    IReadOnlyDictionary<string, string> values)
  {
    foreach ((string key, string value) in values)
    {
      string normalized = NormalizeKey(key);
      switch (normalized)
      {
        case "server":
        case "datasource":
        case "address":
        case "addr":
        case "networkaddress":
          (string host, int? port) = ParseServer(value);
          options = options with
          {
            Host = host,
            Port = port ?? options.Port,
          };
          break;
        case "port":
          options = options with { Port = ParsePort(value) };
          break;
        case "userid":
        case "uid":
        case "user":
        case "username":
          options = options with { Username = value };
          break;
        case "password":
        case "pwd":
          options = options with { Password = value };
          break;
        case "database":
        case "initialcatalog":
          options = options with { Database = value };
          break;
        case "encrypt":
        case "encryptionmode":
          options = options with { EncryptionMode = ParseEncryptionMode(value) };
          break;
        case "trustservercertificate":
          options = options with { TrustServerCertificate = ParseBoolean(value, key) };
          break;
        case "hostnameincertificate":
        case "tlshostname":
          options = options with { TlsHostName = value };
          break;
        case "applicationname":
        case "app":
          options = options with { ApplicationName = value };
          break;
        case "workstationid":
          options = options with { WorkstationId = value };
          break;
        case "packetsize":
          options = options with { PacketSize = ParsePositiveInt(value, key) };
          break;
        case "connecttimeout":
        case "connectiontimeout":
          options = options with
          {
            ConnectTimeout = TimeSpan.FromSeconds(ParseNonNegativeInt(value, key)),
          };
          break;
        case "stringcachecapacity":
          options = options with
          {
            StringCacheCapacity = ParseNonNegativeInt(value, key),
          };
          break;
        case "stringcachemaximumbytelength":
          options = options with
          {
            StringCacheMaximumByteLength = ParseNonNegativeInt(value, key),
          };
          break;
        default:
          throw new FormatException($"Unsupported SQL Server connection option '{key}'.");
      }
    }

    return options;
  }

  private static (string Host, int? Port) ParseServer(string value)
  {
    string server = value.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)
      ? value[4..]
      : value;
    if (server.Length == 0)
    {
      throw new FormatException("SQL Server host cannot be empty.");
    }

    int separator = server.LastIndexOf(',');
    if (separator < 0)
    {
      return (server, null);
    }

    string host = server[..separator].Trim();
    string port = server[(separator + 1)..].Trim();
    if (host.Length == 0)
    {
      throw new FormatException("SQL Server host cannot be empty.");
    }

    return (host, ParsePort(port));
  }

  private static int ParsePort(string value) =>
    int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) &&
    port is > 0 and <= ushort.MaxValue
      ? port
      : throw new FormatException($"Invalid SQL Server port '{value}'.");

  private static int ParsePositiveInt(string value, string name) =>
    int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
    parsed > 0
      ? parsed
      : throw new FormatException($"Invalid SQL Server {name} value '{value}'.");

  private static int ParseNonNegativeInt(string value, string name) =>
    int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
    parsed >= 0
      ? parsed
      : throw new FormatException($"Invalid SQL Server {name} value '{value}'.");

  private static bool ParseBoolean(string value, string name) =>
    value.Trim().ToLowerInvariant() switch
    {
      "true" or "yes" or "1" => true,
      "false" or "no" or "0" => false,
      _ => throw new FormatException($"Invalid SQL Server {name} value '{value}'."),
    };

  private static MsSqlEncryptionMode ParseEncryptionMode(string value) =>
    value.Trim().ToLowerInvariant() switch
    {
      "false" or "no" or "off" or "disable" or "disabled" =>
        MsSqlEncryptionMode.Disable,
      "optional" => MsSqlEncryptionMode.Optional,
      "true" or "yes" or "on" or "mandatory" or "require" or "required" =>
        MsSqlEncryptionMode.Require,
      "strict" => MsSqlEncryptionMode.Strict,
      _ => throw new FormatException($"Invalid SQL Server encryption mode '{value}'."),
    };

  private static string NormalizeKey(string key)
  {
    const int maximumStackLength = 256;
    Span<char> buffer = key.Length <= maximumStackLength
      ? stackalloc char[key.Length]
      : new char[key.Length];
    int length = 0;
    foreach (char character in key)
    {
      if (character is not (' ' or '_' or '-'))
      {
        buffer[length++] = char.ToLowerInvariant(character);
      }
    }

    return new string(buffer[..length]);
  }
}
