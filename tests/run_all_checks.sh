#!/usr/bin/env bash
# Runs every check the project has, in order: build → unit tests → start the app → API checks → security checks.
# Used by the GitHub Actions pipeline and safe to run locally (it uses its own throw-away database).
#
#   bash tests/run_all_checks.sh
set -euo pipefail

cd "$(dirname "$0")/.."
APP_URL="http://127.0.0.1:5099"
CHECK_DATABASE="App_Data/AcxiomCRM-checks.db"
APP_LOG="$(mktemp)"

echo "== 1/5 Build (Release, warnings are errors)"
dotnet build AcxiomCRM.sln -c Release -warnaserror

echo "== 2/5 Unit tests"
dotnet test AcxiomCRM.sln -c Release --no-build

echo "== 3/5 Start the app on a fresh demo database"
rm -f "$CHECK_DATABASE" "$CHECK_DATABASE-shm" "$CHECK_DATABASE-wal"
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__DefaultConnection="Data Source=$CHECK_DATABASE" \
    dotnet run --project AcxiomCRM.csproj -c Release --no-build --no-launch-profile --urls "$APP_URL" > "$APP_LOG" 2>&1 &
APP_PROCESS_ID=$!
trap 'kill $APP_PROCESS_ID 2>/dev/null || true' EXIT

for attempt in $(seq 1 60); do
    if curl -s -o /dev/null -w "%{http_code}" "$APP_URL/Account/Login" | grep -q 200; then
        break
    fi
    if [ "$attempt" = 60 ]; then
        echo "App did not start:"; cat "$APP_LOG"; exit 1
    fi
    sleep 1
done

echo "== 4/5 API and workflow checks"
python3 tests/check_api.py "$APP_URL"

echo "== 5/5 Security checks (waiting for a fresh sign-in rate-limit window first)"
sleep 61
CHECK_DATABASE_FILE="$CHECK_DATABASE" python3 tests/check_security.py "$APP_URL"

if grep -q "fail:" "$APP_LOG"; then
    echo "The server logged errors during the checks:"; grep -A5 "fail:" "$APP_LOG"; exit 1
fi
echo "All checks passed."
