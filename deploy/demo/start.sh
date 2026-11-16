#!/bin/sh
# Starts the API (port 8080, the one Render routes to) and a worker (port 8081, health only) sharing one file store.
set -eu

export Storage__Root="${Storage__Root:-/tmp/data}"
mkdir -p "$Storage__Root"

cd /app/api
ASPNETCORE_HTTP_PORTS=8080 dotnet BackgroundJobs.Api.dll &
api=$!

cd /app/worker
# The API prepares the database; the worker waits a little so it does not race that on a cold start.
(sleep 20 && ASPNETCORE_HTTP_PORTS=8081 exec dotnet BackgroundJobs.Worker.dll) &
worker=$!

trap 'kill "$api" "$worker" 2>/dev/null' TERM INT
wait "$api"
status=$?
kill "$worker" 2>/dev/null || true
exit "$status"
