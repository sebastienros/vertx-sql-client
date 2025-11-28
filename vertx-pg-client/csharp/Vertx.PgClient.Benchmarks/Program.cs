// Copyright (C) 2017 Julien Viet
// Licensed under the Apache License, Version 2.0

using BenchmarkDotNet.Running;
using Vertx.PgClient.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
