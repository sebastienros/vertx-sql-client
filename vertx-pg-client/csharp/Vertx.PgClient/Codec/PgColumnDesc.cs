// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

namespace Vertx.PgClient.Codec;

/// <summary>
/// PostgreSQL column description.
/// </summary>
public sealed class PgColumnDesc
{
    public static readonly PgColumnDesc[] EmptyColumns = Array.Empty<PgColumnDesc>();

    public string Name { get; }
    public int RelationId { get; }
    public int TypeOid { get; }
    public DataType DataType { get; }
    public DataFormat DataFormat { get; }
    public short RelationAttributeNo { get; }
    public short Length { get; }
    public int TypeModifier { get; }

    public PgColumnDesc(
        string name,
        int relationId,
        short relationAttributeNo,
        DataType dataType,
        short length,
        int typeModifier,
        DataFormat dataFormat)
    {
        Name = name;
        RelationId = relationId;
        RelationAttributeNo = relationAttributeNo;
        DataType = dataType;
        TypeOid = (int)dataType.Id;
        Length = length;
        TypeModifier = typeModifier;
        DataFormat = dataFormat;
    }

    public PgColumnDesc(
        string name,
        int relationId,
        short relationAttributeNo,
        DataType dataType,
        int typeOid,
        short length,
        int typeModifier,
        DataFormat dataFormat)
    {
        Name = name;
        RelationId = relationId;
        RelationAttributeNo = relationAttributeNo;
        DataType = dataType;
        TypeOid = typeOid;
        Length = length;
        TypeModifier = typeModifier;
        DataFormat = dataFormat;
    }

    /// <summary>
    /// Creates a simple column descriptor with just a name.
    /// </summary>
    public static PgColumnDesc ForName(string name) => new(name, 0, 0, DataType.Unknown, 0, 0, DataFormat.Text);

    public bool SupportsBinary => DataType.SupportsBinary;

    public bool HasTextFormat => DataFormat == DataFormat.Text;

    public bool IsArray => DataType.IsArray;

    public PgColumnDesc ToBinaryDataFormat()
    {
        return new PgColumnDesc(
            Name,
            RelationId,
            RelationAttributeNo,
            DataType,
            Length,
            TypeModifier,
            DataFormat.Binary);
    }

    public Type ClrType => DataType.ClrType;
}
