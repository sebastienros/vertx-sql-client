/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Buffers.Binary;
using Apex.PgClient.Internal;
using Apex.SqlClient;

namespace Apex.PgClient.Tests;

[TestClass]
public sealed class PgRowDecoderTests
{
  [TestMethod]
  public void TypedScalarGetterValidatesPostgreSqlType()
  {
    PgRowDecoder decoder = new(16, 64);
    byte[] row = CreateRow([0x3f, 0x80, 0x00, 0x00]);
    SqlColumn column = new(
      "value",
      TypeId: 700,
      TypeSize: sizeof(float),
      TypeModifier: -1,
      SqlDataFormat.Binary);

    Assert.ThrowsExactly<InvalidCastException>(
      () => decoder.Decode<int>(row, 0, column));
  }

  [TestMethod]
  public void UnknownBinaryTypeIsNotDecodedAsUtf8()
  {
    PgRowDecoder decoder = new(16, 64);
    byte[] row = CreateRow([0xff]);
    SqlColumn column = new(
      "value",
      TypeId: 99999,
      TypeSize: -1,
      TypeModifier: -1,
      SqlDataFormat.Binary);

    Assert.ThrowsExactly<PgUnsupportedTypeException>(
      () => decoder.Decode(row, 0, column));
  }

  private static byte[] CreateRow(ReadOnlySpan<byte> value)
  {
    byte[] row = new byte[sizeof(short) + sizeof(int) + value.Length];
    BinaryPrimitives.WriteInt16BigEndian(row, 1);
    BinaryPrimitives.WriteInt32BigEndian(row.AsSpan(sizeof(short)), value.Length);
    value.CopyTo(row.AsSpan(sizeof(short) + sizeof(int)));
    return row;
  }
}
