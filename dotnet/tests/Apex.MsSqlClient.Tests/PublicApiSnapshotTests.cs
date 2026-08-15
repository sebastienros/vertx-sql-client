/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using Apex.Tests.Shared;

namespace Apex.MsSqlClient.Tests;

[TestClass]
public sealed class PublicApiSnapshotTests
{
  [TestMethod]
  public void MsSqlApiMatchesApprovedSnapshot() =>
    PublicApiSnapshot.Verify(typeof(MsSqlClient).Assembly, "Apex.MsSqlClient.txt");
}
