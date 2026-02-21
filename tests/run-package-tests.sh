#!/usr/bin/env bash
# ============================================================
# SchemaForge - Package Integration Test Runner
#
# Tests SchemaForge as a published NuGet package in two ways:
#   1. CLI tool  — installs SchemaForge.Cli and runs `schemaforge` command
#   2. Library   — dotnet test project that uses the fluent API via NuGet refs
#
# Usage:
#   bash tests/run-package-tests.sh [--skip-oracle] [--keep-containers] [--skip-cli] [--skip-library]
#
# Prerequisites:
#   - Docker running
#   - dotnet 9 SDK on PATH
# ============================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$PROJECT_DIR/docker-compose.test.yml"
NUPKG_DIR="$PROJECT_DIR/nupkg"
LOG_DIR="$SCRIPT_DIR/logs"
SEED_SQL="$SCRIPT_DIR/seed-sqlserver.sql"

KEEP_CONTAINERS=false
SKIP_ORACLE=false
SKIP_CLI=false
SKIP_LIBRARY=false

for arg in "$@"; do
    case $arg in
        --keep-containers) KEEP_CONTAINERS=true ;;
        --skip-oracle)     SKIP_ORACLE=true ;;
        --skip-cli)        SKIP_CLI=true ;;
        --skip-library)    SKIP_LIBRARY=true ;;
    esac
done

# ============================================================
# Colors
# ============================================================
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
BLUE='\033[0;34m'; CYAN='\033[0;36m'; BOLD='\033[1m'; NC='\033[0m'

log()    { echo -e "${BLUE}[$(date +%H:%M:%S)]${NC} $*"; }
pass()   { echo -e "${GREEN}  PASS${NC} $*"; PASS=$((PASS+1)); RESULTS="${RESULTS}PASS  $*"$'\n'; }
fail()   { echo -e "${RED}  FAIL${NC} $*"; FAIL=$((FAIL+1)); RESULTS="${RESULTS}FAIL  $*"$'\n'; }
header() { echo -e "\n${CYAN}${BOLD}=== $* ===${NC}"; }

PASS=0; FAIL=0; RESULTS=""
dc() { docker compose -f "$COMPOSE_FILE" "$@"; }

# ============================================================
# Cleanup on exit
# ============================================================
cleanup() {
    if [ "$KEEP_CONTAINERS" = false ]; then
        log "Stopping containers..."
        dc down -v 2>/dev/null || true
    fi
    # Uninstall CLI tool if it was installed by this script
    if [ "${CLI_INSTALLED:-false}" = true ]; then
        log "Uninstalling schemaforge CLI tool..."
        dotnet tool uninstall --global SchemaForge.Cli 2>/dev/null || true
    fi
}
trap cleanup EXIT

# ============================================================
# Step 1: Build all packages
# ============================================================
header "Step 1: Build NuGet Packages"
log "Running pack.sh (Release build)..."
if ! bash "$PROJECT_DIR/pack.sh" Release; then
    echo -e "${RED}Pack failed. Aborting.${NC}"
    exit 1
fi

# ============================================================
# Step 2: Start Docker containers
# ============================================================
header "Step 2: Start Database Containers"
dc up -d

# ============================================================
# Step 3: Wait for databases
# ============================================================
header "Step 3: Wait for Databases"
SQLSERVER_READY=false
POSTGRES_READY=false
MYSQL_READY=false
ORACLE_READY=false

wait_for() {
    local name="$1" max="$2"; shift 2
    local elapsed=0
    log "Waiting for $name..."
    while [ $elapsed -lt "$max" ]; do
        if "$@" >/dev/null 2>&1; then
            log "$name ready (${elapsed}s)"; return 0
        fi
        sleep 3; elapsed=$((elapsed+3))
    done
    log "$name failed to start within ${max}s"; return 1
}

wait_for "SQL Server" 120 \
    dc exec -T sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "SchemaForge@Test1" -Q "SELECT 1" \
    && SQLSERVER_READY=true || true

wait_for "PostgreSQL" 60 \
    dc exec -T postgres pg_isready -U postgres -d schemaforge_test \
    && POSTGRES_READY=true || true

wait_for "MySQL" 60 \
    dc exec -T mysql mysqladmin ping -h localhost -uroot -pSchemaForgeTest1 \
    && MYSQL_READY=true || true

if [ "$SKIP_ORACLE" = false ]; then
    wait_for "Oracle" 180 \
        dc exec -T oracle healthcheck.sh \
        && ORACLE_READY=true || true
fi

# ============================================================
# Step 4: Seed SQL Server
# ============================================================
header "Step 4: Seed SQL Server"
if [ "$SQLSERVER_READY" = true ]; then
    log "Creating database and seeding test data..."
    dc exec -T sqlserver /opt/mssql-tools/bin/sqlcmd \
        -S localhost -U sa -P "SchemaForge@Test1" \
        -Q "IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name='schemaforge_test') CREATE DATABASE schemaforge_test;" \
        2>/dev/null || true

    cat "$SEED_SQL" | dc exec -T sqlserver /opt/mssql-tools/bin/sqlcmd \
        -S localhost -U sa -P "SchemaForge@Test1" -d schemaforge_test \
        2>/dev/null | tail -5 || true
    log "SQL Server seeded"
else
    echo -e "${RED}SQL Server not ready — cannot run any tests.${NC}"
    exit 1
fi

mkdir -p "$LOG_DIR"

# ============================================================
# Step 5: CLI Tool Test
# ============================================================
if [ "$SKIP_CLI" = false ]; then
    header "Step 5: CLI Tool Package Test"
    echo "  Installing SchemaForge.Cli from local nupkg..."

    # Ensure ~/.dotnet/tools is on PATH for this session
    export PATH="$PATH:$HOME/.dotnet/tools"

    # Uninstall any existing version first
    dotnet tool uninstall --global SchemaForge.Cli 2>/dev/null || true

    install_out=$(dotnet tool install --global SchemaForge.Cli \
        --add-source "$NUPKG_DIR" \
        --version 1.0.5 2>&1)
    install_code=$?
    echo "$install_out"
    if [ $install_code -eq 0 ]; then
        CLI_INSTALLED=true
        log "schemaforge CLI installed"
        schemaforge --version 2>/dev/null || true
    else
        fail "CLI: failed to install SchemaForge.Cli from $NUPKG_DIR"
        CLI_INSTALLED=false
    fi

    if [ "${CLI_INSTALLED:-false}" = true ]; then

        # ---- Connection string helpers ----
        get_conn() {
            case $1 in
                sqlserver) echo 'Server=localhost,1434;Database=schemaforge_test;User Id=sa;Password=SchemaForge@Test1;TrustServerCertificate=True;' ;;
                postgres)  echo 'Host=localhost;Port=5434;Database=schemaforge_test;Username=postgres;Password=SchemaForgeTest1;' ;;
                mysql)     echo 'Server=localhost;Port=3307;Database=schemaforge_test;User Id=root;Password=SchemaForgeTest1;' ;;
                oracle)    echo 'User Id=testuser;Password=SchemaForgeTest1;Data Source=localhost:1522/FREEPDB1;' ;;
            esac
        }
        get_schema() {
            case $1 in
                sqlserver) echo "dbo" ;;
                postgres)  echo "public" ;;
                mysql)     echo "schemaforge_test" ;;
                oracle)    echo "TESTUSER" ;;
            esac
        }

        # ---- Clean a target database ----
        clean_db() {
            case $1 in
                sqlserver)
                    dc exec -T sqlserver /opt/mssql-tools/bin/sqlcmd \
                        -S localhost -U sa -P "SchemaForge@Test1" -d master \
                        -Q "IF EXISTS (SELECT 1 FROM sys.databases WHERE name='schemaforge_test') BEGIN ALTER DATABASE schemaforge_test SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE schemaforge_test; END; CREATE DATABASE schemaforge_test;" \
                        2>/dev/null || true ;;
                postgres)
                    dc exec -T postgres psql -U postgres -d schemaforge_test \
                        -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;" 2>/dev/null || true ;;
                mysql)
                    dc exec -T mysql mysql -uroot -pSchemaForgeTest1 \
                        -e "DROP DATABASE IF EXISTS schemaforge_test; CREATE DATABASE schemaforge_test;" 2>/dev/null || true ;;
                oracle)
                    dc exec -T oracle sqlplus -s testuser/SchemaForgeTest1@localhost:1521/FREEPDB1 << 'OSQL' 2>/dev/null || true
BEGIN
  FOR t IN (SELECT table_name FROM user_tables) LOOP
    EXECUTE IMMEDIATE 'DROP TABLE "' || t.table_name || '" CASCADE CONSTRAINTS PURGE';
  END LOOP;
  FOR v IN (SELECT view_name FROM user_views) LOOP
    EXECUTE IMMEDIATE 'DROP VIEW "' || v.view_name || '"';
  END LOOP;
END;
/
OSQL
                    ;;
            esac
        }

        # ---- Seed a non-SQL Server DB from SQL Server using the CLI ----
        seed_from_sqlserver() {
            local target="$1"
            log "  Seeding $target from SQL Server..."
            clean_db "$target"
            local seed_log="$LOG_DIR/cli_seed_${target}.log"
            schemaforge \
                --from sqlserver --to "$target" \
                --source-conn "$(get_conn sqlserver)" \
                --target-conn "$(get_conn "$target")" \
                --schema "$(get_schema "$target")" \
                --batch-size 1000 --continue-on-error \
                --no-views --no-indexes --no-constraints --no-foreign-keys \
                > "$seed_log" 2>&1 || true
        }

        # ---- Run a single migration and report pass/fail ----
        run_cli_test() {
            local source="$1" target="$2"
            local label="CLI: $source -> $target"
            local log_file="$LOG_DIR/cli_${source}_to_${target}.log"

            # Re-seed SQL Server with original test data each iteration
            cat "$SEED_SQL" | dc exec -T sqlserver /opt/mssql-tools/bin/sqlcmd \
                -S localhost -U sa -P "SchemaForge@Test1" -d schemaforge_test \
                2>/dev/null | tail -2 || true

            # If source is not SQL Server, seed it from SQL Server first
            if [ "$source" != "sqlserver" ]; then
                seed_from_sqlserver "$source"
            fi

            # Clean the target
            clean_db "$target"

            log "CLI test: $label"
            local exit_code=0
            schemaforge \
                --from "$source" --to "$target" \
                --source-conn "$(get_conn "$source")" \
                --target-conn "$(get_conn "$target")" \
                --schema "$(get_schema "$target")" \
                --batch-size 1000 --continue-on-error \
                --no-views --no-indexes --no-constraints --no-foreign-keys \
                > "$log_file" 2>&1 || exit_code=$?

            if [ $exit_code -eq 0 ]; then
                pass "$label"
            else
                fail "$label  (exit code $exit_code, see $log_file)"
                tail -5 "$log_file"
            fi
        }

        # ---- Determine which DB types are available ----
        CLI_DBS="sqlserver"
        [ "$POSTGRES_READY" = true ] && CLI_DBS="$CLI_DBS postgres"
        [ "$MYSQL_READY"    = true ] && CLI_DBS="$CLI_DBS mysql"
        [ "$ORACLE_READY"   = true ] && CLI_DBS="$CLI_DBS oracle"

        # ---- Run all source x target combinations ----
        for source in $CLI_DBS; do
            for target in $CLI_DBS; do
                [ "$source" = "$target" ] && continue
                run_cli_test "$source" "$target"
            done
        done
    fi
fi

# ============================================================
# Step 6: Library Package Test (dotnet test)
# ============================================================
if [ "$SKIP_LIBRARY" = false ]; then
    header "Step 6: Library Package Test (Fluent API)"
    log "Running dotnet test on SchemaForge.PackageTests..."
    echo ""

    LIB_TEST_DIR="$SCRIPT_DIR/SchemaForge.PackageTests"
    LIB_LOG="$LOG_DIR/library_tests.log"

    # Clear NuGet global packages cache for SchemaForge packages so that
    # the freshly-built packages in nupkg/ are always used (not the cached version).
    log "Clearing NuGet global packages cache for SchemaForge packages..."
    dotnet nuget locals global-packages --clear 2>/dev/null || true

    dotnet test "$LIB_TEST_DIR" \
        --verbosity normal \
        --logger "console;verbosity=normal" \
        2>&1 | tee "$LIB_LOG"
    test_code=${PIPESTATUS[0]}
    if [ $test_code -eq 0 ]; then
        pass "Library API: all package tests passed"
    else
        fail "Library API: one or more package tests failed (see $LIB_LOG)"
    fi
fi

# ============================================================
# Summary
# ============================================================
header "Summary"
echo ""
echo "$RESULTS" | while IFS= read -r r; do
    [ -z "$r" ] && continue
    case "$r" in
        PASS*) echo -e "  ${GREEN}$r${NC}" ;;
        FAIL*) echo -e "  ${RED}$r${NC}" ;;
    esac
done
echo ""
echo -e "  ${GREEN}Passed: $PASS${NC}  ${RED}Failed: $FAIL${NC}"
echo ""
if [ "$FAIL" -gt 0 ]; then
    echo -e "  ${RED}${BOLD}SOME TESTS FAILED${NC}  (logs in $LOG_DIR/)"
    exit 1
else
    echo -e "  ${GREEN}${BOLD}ALL PACKAGE TESTS PASSED${NC}"
fi
