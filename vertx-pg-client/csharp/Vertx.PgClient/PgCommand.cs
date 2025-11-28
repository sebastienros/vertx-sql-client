// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using Vertx.PgClient.Codec;

namespace Vertx.PgClient;

/// <summary>
/// Represents a command that can be scheduled for execution on a PostgreSQL connection.
/// Commands support pipelining - multiple commands can be sent before waiting for responses.
/// </summary>
internal abstract class PgCommand
{
    /// <summary>
    /// Encodes the command to the encoder buffer.
    /// </summary>
    public abstract void Encode(PgEncoder encoder);
    
    /// <summary>
    /// Handles a response message from the server.
    /// </summary>
    /// <returns>True if the command is complete and should be removed from the inflight queue.</returns>
    public abstract bool HandleResponse(Response response);
    
    /// <summary>
    /// Called when the command completes successfully or with an error.
    /// </summary>
    public abstract void Complete(Exception? error = null);
    
    /// <summary>
    /// Gets whether this command expects a ReadyForQuery message to complete.
    /// </summary>
    public virtual bool ExpectsReadyForQuery => true;
}

/// <summary>
/// A simple query command using the simple query protocol.
/// </summary>
internal sealed class SimpleQueryCommand : PgCommand
{
    private readonly string _sql;
    private readonly TaskCompletionSource<RowSet> _tcs;
    private readonly List<Row> _rows = new();
    private PgColumnDesc[]? _columnDesc;
    private int _rowsAffected;
    private PgException? _error;

    public SimpleQueryCommand(string sql)
    {
        _sql = sql;
        _tcs = new TaskCompletionSource<RowSet>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task<RowSet> Task => _tcs.Task;

    public override void Encode(PgEncoder encoder)
    {
        encoder.WriteQuery(_sql);
    }

    public override bool HandleResponse(Response response)
    {
        switch (response)
        {
            case RowDescriptionResponse rd:
                _columnDesc = rd.Columns;
                return false;

            case DataRowResponse dataRow:
                if (_columnDesc is not null)
                {
                    var row = DecodeRow(dataRow.Values, _columnDesc);
                    _rows.Add(row);
                }
                return false;

            case CommandCompleteResponse cmd:
                _rowsAffected = ParseRowsAffected(cmd.Tag);
                return false;

            case EmptyQueryResponse:
                return false;

            case ErrorResponse error:
                _error = new PgException(error.Message, error.Code, error.Severity);
                return false;

            case ReadyForQueryResponse:
                return true; // Command complete

            default:
                return false;
        }
    }

    public override void Complete(Exception? error = null)
    {
        if (error is not null)
        {
            _tcs.TrySetException(error);
        }
        else if (_error is not null)
        {
            _tcs.TrySetException(_error);
        }
        else
        {
            _tcs.TrySetResult(new RowSet(_rows, _columnDesc ?? [], _rowsAffected));
        }
    }

    private static Row DecodeRow(byte[][] values, PgColumnDesc[] columnDesc)
    {        
        var decodedValues = new PgValue[values.Length];
        
        for (int i = 0; i < values.Length; i++)
        {
            var column = columnDesc[i];
            if (values[i] is null)
            {
                decodedValues[i] = PgValue.CreateNull(column.DataType);
            }
            else
            {
                decodedValues[i] = column.DataFormat == DataFormat.Binary
                    ? PgValue.DecodeBinary(column.DataType, values[i])
                    : PgValue.DecodeText(column.DataType, values[i]);
            }
        }

        return new Row(decodedValues, columnDesc);
    }

    private static int ParseRowsAffected(string tag)
    {
        // Tag format: "INSERT 0 5", "UPDATE 5", "DELETE 5", "SELECT 5"
        // Find the last space and parse the number after it
        ReadOnlySpan<char> span = tag.AsSpan();
        int lastSpace = span.LastIndexOf(' ');
        if (lastSpace >= 0 && int.TryParse(span[(lastSpace + 1)..], out int count))
        {
            return count;
        }
        return 0;
    }
}
