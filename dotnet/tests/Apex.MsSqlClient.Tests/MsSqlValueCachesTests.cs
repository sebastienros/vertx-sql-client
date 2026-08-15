/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Text;
using Apex.MsSqlClient.Internal;

namespace Apex.MsSqlClient.Tests;

[TestClass]
public sealed class MsSqlValueCachesTests
{
  [TestMethod]
  public void CachesSmallRepeatedStringsAfterSecondObservation()
  {
    MsSqlStringCache cache = new(capacity: 16, maximumByteLength: 128);
    byte[] bytes = Encoding.Unicode.GetBytes("repeated");

    string first = cache.GetString(bytes, 1200);
    string second = cache.GetString(bytes, 1200);
    string third = cache.GetString(bytes, 1200);

    Assert.AreEqual(first, second);
    Assert.AreSame(second, third);
  }

  [TestMethod]
  public void KeepsCodePagesAndOversizedValuesOutOfWrongEntries()
  {
    MsSqlStringCache cache = new(capacity: 1, maximumByteLength: 2);

    Assert.AreEqual("€", cache.GetString([0x80], 1252));
    Assert.AreNotEqual("€", cache.GetString([0x80], 437));
    string first = cache.GetString(Encoding.Unicode.GetBytes("large"), 1200);
    string second = cache.GetString(Encoding.Unicode.GetBytes("large"), 1200);
    Assert.AreNotSame(first, second);
  }

  [TestMethod]
  public void DisablingClearsCachedReferences()
  {
    MsSqlStringCache cache = new(capacity: 16, maximumByteLength: 128);
    byte[] bytes = Encoding.UTF8.GetBytes("cached");
    _ = cache.GetString(bytes, 65001);
    string cached = cache.GetString(bytes, 65001);
    Assert.AreSame(cached, cache.GetString(bytes, 65001));

    cache.Disable();

    Assert.AreNotSame(cached, cache.GetString(bytes, 65001));
  }
}
