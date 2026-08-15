/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using Apex.SqlClient.Internal;

namespace Apex.SqlClient.Tests;

[TestClass]
public sealed class SqlRowPageTests
{
  [TestMethod]
  public void DecodesPageValuesLazily()
  {
    CountingDecoder decoder = new();
    SqlColumn[] columns = [new("value", 23, 4, -1, SqlDataFormat.Binary)];
    SqlRowPageBuilder builder = new(decoder, rowCapacity: 2, byteCapacity: 8);
    builder.Add([1]);
    builder.Add([2]);

    SqlRow[] rows = builder.Build(columns);

    Assert.AreEqual(0, decoder.DecodeCount);
    Assert.AreEqual(1, rows[0].GetInt32(0));
    Assert.AreEqual(1, decoder.DecodeCount);
    Assert.AreEqual(2, rows[1].Get<int>(0));
    Assert.AreEqual(2, decoder.DecodeCount);
  }

  [TestMethod]
  public void BuildsLargeResultsAcrossBoundedPages()
  {
    CountingDecoder decoder = new();
    SqlColumn[] columns = [new("value", 23, 4, -1, SqlDataFormat.Binary)];
    SqlRowPageCollectionBuilder builder = new(decoder);
    for (int i = 0; i < 300; i++)
    {
      builder.Add([(byte)i]);
    }

    SqlRow[] rows = builder.Build(columns);

    Assert.HasCount(300, rows);
    Assert.AreEqual(0, rows[0].GetInt32(0));
    Assert.AreEqual(43, rows[299].GetInt32(0));
  }

  private sealed class CountingDecoder : ISqlRowDecoder
  {
    public int DecodeCount { get; private set; }

    public int GetFieldCount(ReadOnlySpan<byte> row) => 1;

    public bool IsNull(ReadOnlySpan<byte> row, int ordinal) => false;

    public object Decode(
      ReadOnlySpan<byte> row,
      int ordinal,
      SqlColumn column)
    {
      DecodeCount++;
      return (int)row[0];
    }

    public T Decode<T>(
      ReadOnlySpan<byte> row,
      int ordinal,
      SqlColumn column)
    {
      DecodeCount++;
      object value = (int)row[0];
      return (T)value;
    }
  }
}
