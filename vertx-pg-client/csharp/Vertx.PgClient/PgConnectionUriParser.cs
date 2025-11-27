// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Text.RegularExpressions;
using System.Web;

namespace Vertx.PgClient;

/// <summary>
/// Parser for PostgreSQL connection URIs.
/// Based on PostgreSQL 11: postgresql://[user[:password]@][netloc][:port][,...][/dbname][?param1=value1&amp;...]
/// </summary>
internal static partial class PgConnectionUriParser
{
    private const string SchemeDesignatorRegex = @"postgre(s|sql)://";
    private const string UserInfoRegex = @"((?<userinfo>[a-zA-Z0-9\-._~%!*]+(:[a-zA-Z0-9\-._~%!*]+)?)@)?";
    private const string NetLocationRegex = @"(?<netloc>[0-9.]+|\[[a-zA-Z0-9:]+\]|[a-zA-Z0-9\-._~%]+)?";
    private const string PortRegex = @"(:(?<port>\d+))?";
    private const string DatabaseRegex = @"(/(?<database>[a-zA-Z0-9\-._~%!*]+))?";
    private const string ParamsRegex = @"(\?(?<params>.*))?";

    private static readonly Regex SchemeDesignatorPattern = new($"^{SchemeDesignatorRegex}", RegexOptions.Compiled);
    
    private static readonly Regex FullUriPattern = new(
        $"^{SchemeDesignatorRegex}{UserInfoRegex}{NetLocationRegex}{PortRegex}{DatabaseRegex}{ParamsRegex}$",
        RegexOptions.Compiled);

    public static PgConnectOptions Parse(string connectionUri)
    {
        return Parse(connectionUri, exact: true);
    }

    public static PgConnectOptions Parse(string connectionUri, bool exact)
    {
        try
        {
            var matcher = SchemeDesignatorPattern.Match(connectionUri);
            if (matcher.Success || exact)
            {
                var options = new PgConnectOptions();
                DoParse(connectionUri, options);
                return options;
            }
            else
            {
                throw new ArgumentException($"Cannot parse invalid connection URI: {connectionUri}");
            }
        }
        catch (Exception e) when (e is not ArgumentException)
        {
            throw new ArgumentException($"Cannot parse invalid connection URI: {connectionUri}", e);
        }
    }

    private static void DoParse(string connectionUri, PgConnectOptions options)
    {
        var match = FullUriPattern.Match(connectionUri);

        if (match.Success)
        {
            ParseUserAndPassword(match.Groups["userinfo"].Value, options);
            ParseNetLocation(match.Groups["netloc"].Value, options);
            ParsePort(match.Groups["port"].Value, options);
            ParseDatabaseName(match.Groups["database"].Value, options);
            ParseParameters(match.Groups["params"].Value, options);
        }
        else
        {
            throw new ArgumentException("Wrong syntax of connection URI");
        }
    }

    private static void ParseUserAndPassword(string? userInfo, PgConnectOptions options)
    {
        if (string.IsNullOrEmpty(userInfo))
        {
            return;
        }

        if (OccursExactlyOnce(userInfo, ':'))
        {
            var index = userInfo.IndexOf(':');
            var user = userInfo[..index];
            if (string.IsNullOrEmpty(user))
            {
                throw new ArgumentException("Cannot only specify the password without a concrete user");
            }
            var password = userInfo[(index + 1)..];
            options.User = DecodeUrl(user);
            options.Password = DecodeUrl(password);
        }
        else if (!userInfo.Contains(':'))
        {
            options.User = DecodeUrl(userInfo);
        }
        else
        {
            throw new ArgumentException("Cannot use multiple delimiters to delimit user and password");
        }
    }

    private static void ParseNetLocation(string? hostInfo, PgConnectOptions options)
    {
        if (string.IsNullOrEmpty(hostInfo))
        {
            return;
        }
        ParseNetLocationValue(DecodeUrl(hostInfo), options);
    }

    private static void ParsePort(string? portInfo, PgConnectOptions options)
    {
        if (string.IsNullOrEmpty(portInfo))
        {
            return;
        }

        if (!int.TryParse(DecodeUrl(portInfo), out var port))
        {
            throw new ArgumentException("The port must be a valid integer");
        }

        if (port > 65535 || port <= 0)
        {
            throw new ArgumentException("The port can only range in 1-65535");
        }

        options.Port = port;
    }

    private static void ParseDatabaseName(string? databaseInfo, PgConnectOptions options)
    {
        if (string.IsNullOrEmpty(databaseInfo))
        {
            return;
        }
        options.Database = DecodeUrl(databaseInfo);
    }

    private static void ParseParameters(string? parametersInfo, PgConnectOptions options)
    {
        if (string.IsNullOrEmpty(parametersInfo))
        {
            return;
        }

        foreach (var parameterPair in parametersInfo.Split('&'))
        {
            if (string.IsNullOrEmpty(parameterPair))
            {
                continue;
            }

            var indexOfDelimiter = parameterPair.IndexOf('=');
            if (indexOfDelimiter < 0)
            {
                throw new ArgumentException($"Missing delimiter '=' of parameters \"{parametersInfo}\" in the part \"{parameterPair}\"");
            }
            else
            {
                var key = parameterPair[..indexOfDelimiter].ToLowerInvariant();
                var value = DecodeUrl(parameterPair[(indexOfDelimiter + 1)..].Trim());

                switch (key)
                {
                    case "port":
                        ParsePort(value, options);
                        break;
                    case "host":
                        ParseNetLocationValue(value, options);
                        break;
                    case "hostaddr":
                        options.Host = value;
                        break;
                    case "user":
                        options.User = value;
                        break;
                    case "password":
                        options.Password = value;
                        break;
                    case "dbname":
                        options.Database = value;
                        break;
                    case "sslmode":
                        options.SslMode = SslModeExtensions.Parse(value);
                        break;
                    default:
                        options.Properties[key] = value;
                        break;
                }
            }
        }
    }

    private static void ParseNetLocationValue(string hostValue, PgConnectOptions options)
    {
        if (IsRegardedAsIpv6Address(hostValue))
        {
            options.Host = hostValue[1..^1];
        }
        else
        {
            options.Host = hostValue;
        }
    }

    private static bool IsRegardedAsIpv6Address(string hostAddress)
    {
        return hostAddress.StartsWith('[') && hostAddress.EndsWith(']');
    }

    private static string DecodeUrl(string url)
    {
        return HttpUtility.UrlDecode(url);
    }

    private static bool OccursExactlyOnce(string uri, char character)
    {
        return uri.Contains(character) && uri.IndexOf(character) == uri.LastIndexOf(character);
    }
}
