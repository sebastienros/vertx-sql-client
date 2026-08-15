/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

namespace Apex.MsSqlClient;

public sealed record MsSqlInfo(
  int Number,
  byte State,
  byte Severity,
  string Message,
  string ServerName,
  string ProcedureName,
  int LineNumber);
