#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

dotnet build \
  --configuration Release \
  dotnet/benchmarks/Apex.PipeliningApplication/Apex.PipeliningApplication.csproj
mvn -f dotnet/benchmarks/java/pom.xml -DskipTests package

dotnet run \
  --configuration Release \
  --no-build \
  --project dotnet/benchmarks/Apex.PipeliningApplication
java -cp dotnet/benchmarks/java/target/benchmarks.jar \
  io.vertx.benchmarks.PipeliningApplication