/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

namespace Apex.SqlClient.Tests;

[TestClass]
public sealed class SqlRowTests
{
  private static readonly SqlColumn[] Columns =
  [
      new("id", 23, 4, -1, SqlDataFormat.Binary),
        new("message", 25, -1, -1, SqlDataFormat.Text),
    ];

  [TestMethod]
  public void GetsValuesByOrdinalAndName()
  {
    SqlRow row = new TestRowDecoder().CreateRow(
      Columns,
      1,
      "hello");

    Assert.AreEqual(1, row.Get<int>(0));
    Assert.AreEqual(1, row.Get<int?>(0));
    Assert.AreEqual(1, row.Get<object>(0));
    Assert.AreEqual("hello", row.Get<string>(row.GetOrdinal("message")));
    Assert.AreEqual("hello", row["message"]);
  }

  [TestMethod]
  public void NameLookupIsOrdinal()
  {
    SqlRow row = new TestRowDecoder().CreateRow(
      Columns,
      1,
      "hello");

    Assert.ThrowsExactly<IndexOutOfRangeException>(() => row.GetOrdinal("MESSAGE"));
  }

  [TestMethod]
  public void NullValueCannotBeReadAsNonNullableValueType()
  {
    SqlRow row = new TestRowDecoder().CreateRow(
      Columns,
      null,
      "hello");

    Assert.IsTrue(row.IsNull(0));
    Assert.IsNull(row.Get<int?>(0));
    Assert.ThrowsExactly<InvalidCastException>(() => row.Get<int>(0));
  }

  [TestMethod]
  public void TypedInt32AccessDoesNotAllocate()
  {
    TestRowDecoder decoder = new();
    SqlRow row = decoder.CreateRow(
      [new("id", 23, 4, -1, SqlDataFormat.Binary)],
      42);
    for (int i = 0; i < 1000; i++)
    {
      _ = row.GetInt32(0);
      _ = row.Get<int>(0);
    }

    long before = GC.GetAllocatedBytesForCurrentThread();
    int sum = 0;
    for (int i = 0; i < 10_000; i++)
    {
      sum += row.GetInt32(0);
      sum += row.Get<int>(0);
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.AreEqual(840_000, sum);
    Assert.AreEqual(0, allocated);
  }
}
