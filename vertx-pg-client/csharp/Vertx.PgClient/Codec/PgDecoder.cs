// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Text;

namespace Vertx.PgClient.Codec;

/// <summary>
/// Decodes PostgreSQL backend messages.
/// </summary>
internal sealed class PgDecoder
{
    /// <summary>
    /// Attempts to parse a message from the buffer.
    /// Returns true if a complete message was parsed.
    /// </summary>
    public bool TryParse(ReadOnlySpan<byte> buffer, out Response? response, out int bytesConsumed)
    {
        response = null;
        bytesConsumed = 0;

        // Need at least 5 bytes (1 byte type + 4 bytes length)
        if (buffer.Length < 5)
            return false;

        byte messageType = buffer[0];
        int length = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(1, 4));

        // Length includes itself but not the type byte
        int totalLength = 1 + length;
        if (buffer.Length < totalLength)
            return false;

        var payload = buffer.Slice(5, length - 4);
        response = ParseMessage(messageType, payload);
        bytesConsumed = totalLength;
        return true;
    }

    private Response ParseMessage(byte messageType, ReadOnlySpan<byte> payload)
    {
        return messageType switch
        {
            PgProtocolConstants.AuthenticationRequest => ParseAuthentication(payload),
            PgProtocolConstants.BackendKeyData => ParseBackendKeyData(payload),
            PgProtocolConstants.BindComplete => new BindCompleteResponse(),
            PgProtocolConstants.CloseComplete => new CloseCompleteResponse(),
            PgProtocolConstants.CommandComplete => ParseCommandComplete(payload),
            PgProtocolConstants.CopyData => new CopyDataResponse(payload.ToArray()),
            PgProtocolConstants.CopyDone => new CopyDoneResponse(),
            PgProtocolConstants.CopyInResponse => ParseCopyResponse(payload, isCopyIn: true),
            PgProtocolConstants.CopyOutResponse => ParseCopyResponse(payload, isCopyIn: false),
            PgProtocolConstants.DataRow => ParseDataRow(payload),
            PgProtocolConstants.EmptyQueryResponse => new EmptyQueryResponse(),
            PgProtocolConstants.ErrorResponse => ParseErrorOrNotice(payload, isError: true),
            PgProtocolConstants.NoData => new NoDataResponse(),
            PgProtocolConstants.NoticeResponse => ParseErrorOrNotice(payload, isError: false),
            PgProtocolConstants.NotificationResponse => ParseNotification(payload),
            PgProtocolConstants.ParameterDescription => ParseParameterDescription(payload),
            PgProtocolConstants.ParameterStatus => ParseParameterStatus(payload),
            PgProtocolConstants.ParseComplete => new ParseCompleteResponse(),
            PgProtocolConstants.PortalSuspended => new PortalSuspendedResponse(),
            PgProtocolConstants.ReadyForQuery => ParseReadyForQuery(payload),
            PgProtocolConstants.RowDescription => ParseRowDescription(payload),
            _ => new UnknownResponse(messageType, payload.ToArray())
        };
    }

    private Response ParseAuthentication(ReadOnlySpan<byte> payload)
    {
        int authType = BinaryPrimitives.ReadInt32BigEndian(payload);
        return authType switch
        {
            0 => new AuthenticationOkResponse(),
            3 => new AuthenticationCleartextPasswordResponse(),
            5 => new AuthenticationMd5PasswordResponse(payload.Slice(4, 4).ToArray()),
            10 => new AuthenticationSASLResponse(ParseSaslMechanisms(payload.Slice(4))),
            11 => new AuthenticationSASLContinueResponse(payload.Slice(4).ToArray()),
            12 => new AuthenticationSASLFinalResponse(payload.Slice(4).ToArray()),
            _ => new AuthenticationUnknownResponse(authType)
        };
    }

    private string[] ParseSaslMechanisms(ReadOnlySpan<byte> payload)
    {
        var mechanisms = new List<string>();
        int pos = 0;
        
        while (pos < payload.Length)
        {
            var str = BufferUtils.ReadCString(payload.Slice(pos), out int bytesRead);
            if (string.IsNullOrEmpty(str))
                break;
            mechanisms.Add(str);
            pos += bytesRead;
        }
        
        return mechanisms.ToArray();
    }

    private BackendKeyDataResponse ParseBackendKeyData(ReadOnlySpan<byte> payload)
    {
        int processId = BinaryPrimitives.ReadInt32BigEndian(payload);
        int secretKey = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(4));
        return new BackendKeyDataResponse(processId, secretKey);
    }

    private CommandCompleteResponse ParseCommandComplete(ReadOnlySpan<byte> payload)
    {
        var tag = BufferUtils.ReadCString(payload, out _);
        return new CommandCompleteResponse(tag);
    }

    private CopyResponse ParseCopyResponse(ReadOnlySpan<byte> payload, bool isCopyIn)
    {
        byte format = payload[0];
        short columnCount = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(1));
        var formats = new short[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            formats[i] = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(3 + i * 2));
        }
        return new CopyResponse(format == 1, formats, isCopyIn);
    }

    private DataRowResponse ParseDataRow(ReadOnlySpan<byte> payload)
    {
        short columnCount = BinaryPrimitives.ReadInt16BigEndian(payload);
        var values = new byte[columnCount][];
        int pos = 2;

        for (int i = 0; i < columnCount; i++)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(pos));
            pos += 4;

            if (length == -1)
            {
                values[i] = null!;
            }
            else
            {
                values[i] = payload.Slice(pos, length).ToArray();
                pos += length;
            }
        }

        return new DataRowResponse(values);
    }

    private Response ParseErrorOrNotice(ReadOnlySpan<byte> payload, bool isError)
    {
        var fields = new Dictionary<char, string>();
        int pos = 0;

        while (pos < payload.Length && payload[pos] != 0)
        {
            char fieldType = (char)payload[pos++];
            var value = BufferUtils.ReadCString(payload.Slice(pos), out int bytesRead);
            pos += bytesRead;
            fields[fieldType] = value;
        }

        if (isError)
        {
            return new ErrorResponse(
                fields.GetValueOrDefault('S', "ERROR"),
                fields.GetValueOrDefault('C', ""),
                fields.GetValueOrDefault('M', ""),
                fields.GetValueOrDefault('D'),
                fields.GetValueOrDefault('H'),
                fields.GetValueOrDefault('P'),
                fields.GetValueOrDefault('q'),
                fields.GetValueOrDefault('W'),
                fields.GetValueOrDefault('s'),
                fields.GetValueOrDefault('t'),
                fields.GetValueOrDefault('c'),
                fields.GetValueOrDefault('d'),
                fields.GetValueOrDefault('n'),
                fields.GetValueOrDefault('F'),
                fields.GetValueOrDefault('L'),
                fields.GetValueOrDefault('R')
            );
        }

        return new NoticeResponse(
            fields.GetValueOrDefault('S', "NOTICE"),
            fields.GetValueOrDefault('C', ""),
            fields.GetValueOrDefault('M', ""),
            fields.GetValueOrDefault('D'),
            fields.GetValueOrDefault('H'),
            fields.GetValueOrDefault('P'),
            fields.GetValueOrDefault('W'),
            fields.GetValueOrDefault('F'),
            fields.GetValueOrDefault('L'),
            fields.GetValueOrDefault('R')
        );
    }

    private NotificationResponse ParseNotification(ReadOnlySpan<byte> payload)
    {
        int processId = BinaryPrimitives.ReadInt32BigEndian(payload);
        int pos = 4;
        var channel = BufferUtils.ReadCString(payload.Slice(pos), out int bytesRead);
        pos += bytesRead;
        var payloadStr = BufferUtils.ReadCString(payload.Slice(pos), out _);
        return new NotificationResponse(processId, channel, payloadStr);
    }

    private ParameterDescriptionResponse ParseParameterDescription(ReadOnlySpan<byte> payload)
    {
        short count = BinaryPrimitives.ReadInt16BigEndian(payload);
        var typeOids = new int[count];
        for (int i = 0; i < count; i++)
        {
            typeOids[i] = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(2 + i * 4));
        }
        return new ParameterDescriptionResponse(typeOids);
    }

    private ParameterStatusResponse ParseParameterStatus(ReadOnlySpan<byte> payload)
    {
        var name = BufferUtils.ReadCString(payload, out int bytesRead);
        var value = BufferUtils.ReadCString(payload.Slice(bytesRead), out _);
        return new ParameterStatusResponse(name, value);
    }

    private ReadyForQueryResponse ParseReadyForQuery(ReadOnlySpan<byte> payload)
    {
        char status = (char)payload[0];
        return new ReadyForQueryResponse(status);
    }

    private RowDescriptionResponse ParseRowDescription(ReadOnlySpan<byte> payload)
    {
        short columnCount = BinaryPrimitives.ReadInt16BigEndian(payload);
        var columns = new PgColumnDesc[columnCount];
        int pos = 2;

        for (int i = 0; i < columnCount; i++)
        {
            var name = BufferUtils.ReadCString(payload.Slice(pos), out int bytesRead);
            pos += bytesRead;

            int tableOid = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(pos));
            short columnIndex = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(pos + 4));
            int typeOid = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(pos + 6));
            short typeSize = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(pos + 10));
            int typeModifier = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(pos + 12));
            short formatCode = BinaryPrimitives.ReadInt16BigEndian(payload.Slice(pos + 16));
            pos += 18;

            columns[i] = new PgColumnDesc(
                name,
                tableOid,
                columnIndex,
                DataType.LookupByOid(typeOid),
                typeOid,
                typeSize,
                typeModifier,
                (DataFormat)formatCode
            );
        }

        return new RowDescriptionResponse(columns);
    }
}
