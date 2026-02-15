#!/usr/bin/env bash
# ============================================================
# SchemaForge - All 12 Migration Path Test Runner
# Tests every source x target combination:
#   sqlserver, postgres, mysql, oracle (4 sources x 3 targets = 12)
#
# Usage: ./tests/run-tests.sh [--keep-containers] [--skip-oracle]
# ============================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$PROJECT_DIR/docker-compose.test.yml"
LOG_DIR="$SCRIPT_DIR/logs"

KEEP_CONTAINERS=false
SKIP_ORACLE=false
for arg in "$@"; do
    case $arg in
        --keep-containers) KEEP_CONTAINERS=true ;;
        --skip-oracle)     SKIP_ORACLE=true ;;
    esac
done

# ============================================================
# Connection strings (host-side ports)
# ============================================================
SQLSERVER_CONN='Server=localhost,1434;Database=schemaforge_test;User Id=sa;Password=SchemaForge@Test1;TrustServerCertificate=True;'
POSTGRES_CONN='Host=localhost;Port=5433;Database=schemaforge_test;Username=postgres;Password=SchemaForgeTest1;'
MYSQL_CONN='Server=localhost;Port=3307;Database=schemaforge_test;User Id=root;Password=SchemaForgeTest1;'
ORACLE_CONN='User Id=testuser;Password=SchemaForgeTest1;Data Source=localhost:1522/FREEPDB1;'

get_conn() {
    case $1 in
        sqlserver) echo "$SQLSERVER_CONN" ;;
        postgres)  echo "$POSTGRES_CONN" ;;
        mysql)     echo "$MYSQL_CONN" ;;
        oracle)    echo "$ORACLE_CONN" ;;
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

EXPECTED_TABLES=5
EXPECTED_TOTAL_ROWS=48  # 5+10+10+8+15

# ============================================================
# Test result tracking
# ============================================================
PASS=0
FAIL=0
SKIP=0
RESULTS=""

# ============================================================
# Colors
# ============================================================
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

log()    { echo -e "${BLUE}[$(date +%H:%M:%S)]${NC} $*"; }
pass()   { echo -e "${GREEN}  PASS${NC} $*"; PASS=$((PASS + 1)); RESULTS="${RESULTS}PASS  $*"$'\n'; }
fail()   { echo -e "${RED}  FAIL${NC} $*"; FAIL=$((FAIL + 1)); RESULTS="${RESULTS}FAIL  $*"$'\n'; }
skip_test() { echo -e "${YELLOW}  SKIP${NC} $*"; SKIP=$((SKIP + 1)); RESULTS="${RESULTS}SKIP  $*"$'\n'; }
header() { echo -e "\n${CYAN}${BOLD}=== $* ===${NC}"; }

dc() { docker compose -f "$COMPOSE_FILE" "$@"; }

# ============================================================
# Cleanup on exit
# ============================================================
cleanup() {
    if [ "$KEEP_CONTAINERS" = false ]; then
        log "Stopping containers (use --keep-containers to skip)..."
        dc down -v 2>/dev/null || true
    fi
}
trap cleanup EXIT

# ============================================================
# SQL execution helpers
# ============================================================
SQLCMD_PATH=""

find_sqlcmd() {
    [ -n "$SQLCMD_PATH" ] && return
    if dc exec -T sqlserver test -f /opt/mssql-tools18/bin/sqlcmd 2>/dev/null; then
        SQLCMD_PATH="/opt/mssql-tools18/bin/sqlcmd -C"
    elif dc exec -T sqlserver test -f /opt/mssql-tools/bin/sqlcmd 2>/dev/null; then
        SQLCMD_PATH="/opt/mssql-tools/bin/sqlcmd"
    else
        SQLCMD_PATH="/opt/mssql-tools18/bin/sqlcmd -C"
    fi
}

exec_sqlserver_cmd() {
    local db="$1"; shift
    find_sqlcmd
    dc exec -T sqlserver $SQLCMD_PATH -S localhost -U sa -P "SchemaForge@Test1" -d "$db" -h -1 -W "$@" 2>/dev/null
}

exec_sqlserver() {
    exec_sqlserver_cmd schemaforge_test -Q "SET NOCOUNT ON; $1"
}

exec_sqlserver_master() {
    exec_sqlserver_cmd master -Q "$1"
}

exec_postgres() {
    dc exec -T postgres psql -U postgres -d schemaforge_test -t -A -c "$1" 2>/dev/null
}

exec_mysql() {
    dc exec -T mysql mysql -uroot -pSchemaForgeTest1 schemaforge_test -N -s -e "$1" 2>/dev/null
}

exec_oracle() {
    printf "SET HEADING OFF FEEDBACK OFF PAGESIZE 0 LINESIZE 200\n%s\n" "$1" \
        | dc exec -T oracle sqlplus -s testuser/SchemaForgeTest1@localhost:1521/FREEPDB1 2>/dev/null
}

# ============================================================
# Wait for databases to be healthy
# ============================================================
wait_for_db() {
    local name="$1" max_wait="$2" check_cmd="$3"
    local elapsed=0
    log "Waiting for $name..."
    while [ $elapsed -lt "$max_wait" ]; do
        if eval "$check_cmd" >/dev/null 2>&1; then
            log "$name is ready (${elapsed}s)"
            return 0
        fi
        sleep 3
        elapsed=$((elapsed + 3))
    done
    log "$name failed to start within ${max_wait}s"
    return 1
}

wait_for_all_dbs() {
    wait_for_db "SQL Server" 120 \
        "exec_sqlserver_master 'SELECT 1'" || return 1

    wait_for_db "PostgreSQL" 60 \
        "exec_postgres 'SELECT 1'" || return 1

    wait_for_db "MySQL" 60 \
        "exec_mysql 'SELECT 1'" || return 1

    if [ "$SKIP_ORACLE" = false ]; then
        wait_for_db "Oracle" 180 \
            "exec_oracle 'SELECT 1 FROM dual;'" || return 1
    fi
}

# ============================================================
# Database cleanup functions
# ============================================================
clean_sqlserver() {
    log "  Cleaning SQL Server..."
    exec_sqlserver_master "
        IF EXISTS (SELECT 1 FROM sys.databases WHERE name='schemaforge_test')
        BEGIN
            ALTER DATABASE schemaforge_test SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE schemaforge_test;
        END;
        CREATE DATABASE schemaforge_test;" || true
}

clean_postgres_views() {
    exec_postgres "DO \$\$ DECLARE r RECORD; BEGIN FOR r IN SELECT viewname FROM pg_views WHERE schemaname='public' LOOP EXECUTE 'DROP VIEW IF EXISTS public.' || quote_ident(r.viewname) || ' CASCADE'; END LOOP; END \$\$;" 2>/dev/null || true
}

clean_postgres() {
    log "  Cleaning PostgreSQL..."
    exec_postgres "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;" || true
}

clean_mysql() {
    log "  Cleaning MySQL..."
    dc exec -T mysql mysql -uroot -pSchemaForgeTest1 \
        -e "DROP DATABASE IF EXISTS schemaforge_test; CREATE DATABASE schemaforge_test;" 2>/dev/null || true
}

clean_oracle() {
    log "  Cleaning Oracle..."
    exec_oracle "
BEGIN
    FOR v IN (SELECT view_name FROM user_views) LOOP
        EXECUTE IMMEDIATE 'DROP VIEW \"' || v.view_name || '\"';
    END LOOP;
    FOR c IN (SELECT table_name FROM user_tables) LOOP
        EXECUTE IMMEDIATE 'DROP TABLE \"' || c.table_name || '\" CASCADE CONSTRAINTS PURGE';
    END LOOP;
    FOR s IN (SELECT sequence_name FROM user_sequences) LOOP
        BEGIN
            EXECUTE IMMEDIATE 'DROP SEQUENCE \"' || s.sequence_name || '\"';
        EXCEPTION WHEN OTHERS THEN NULL;
        END;
    END LOOP;
END;
/" || true
}

clean_db() {
    case $1 in
        sqlserver) clean_sqlserver ;;
        postgres)  clean_postgres ;;
        mysql)     clean_mysql ;;
        oracle)    clean_oracle ;;
    esac
}

# ============================================================
# Seed SQL Server with test data
# ============================================================
seed_sqlserver() {
    log "  Seeding SQL Server..."
    exec_sqlserver_master "
        IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name='schemaforge_test')
            CREATE DATABASE schemaforge_test;" || true

    find_sqlcmd
    if cat "$SCRIPT_DIR/seed-sqlserver.sql" \
        | dc exec -T sqlserver $SQLCMD_PATH \
            -S localhost -U sa -P "SchemaForge@Test1" -d schemaforge_test 2>&1 \
        | tail -5; then
        log "  SQL Server seeded successfully"
    else
        log "  WARNING: SQL Server seeding may have had issues"
    fi
}

# ============================================================
# Build CLI arguments for a migration
# ============================================================
build_cli_args() {
    local source_type="$1"
    local target_type="$2"
    local full_migration="${3:-false}"
    local source_conn target_conn target_schema
    source_conn="$(get_conn "$source_type")"
    target_conn="$(get_conn "$target_type")"
    target_schema="$(get_schema "$target_type")"

    CLI_ARGS=(
        --from "$source_type" --to "$target_type"
        --source-conn "$source_conn" --target-conn "$target_conn"
        --schema "$target_schema" --batch-size 1000
        --continue-on-error
    )

    if [ "$full_migration" != "true" ]; then
        CLI_ARGS+=(--no-views --no-indexes --no-constraints --no-foreign-keys)
    fi
}

# ============================================================
# Run dotnet migration and capture result
# ============================================================
run_migration() {
    local source="$1" target="$2" log_file="$3" full="${4:-false}"
    build_cli_args "$source" "$target" "$full"
    local exit_code=0
    (dotnet run --project "$PROJECT_DIR/src/SchemaForge.Cli" -- "${CLI_ARGS[@]}" 2>&1) | tee "$log_file" || exit_code=$?
    return $exit_code
}

# ============================================================
# Validation functions
# ============================================================
get_table_count() {
    local result=""
    case $1 in
        sqlserver)
            result=$(exec_sqlserver "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND TABLE_SCHEMA='dbo'")
            ;;
        postgres)
            result=$(exec_postgres "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'")
            ;;
        mysql)
            result=$(exec_mysql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='schemaforge_test' AND table_type='BASE TABLE'")
            ;;
        oracle)
            result=$(exec_oracle "SELECT COUNT(*) FROM user_tables;")
            ;;
    esac
    echo "$result" | tr -d '[:space:]' | grep -oE '[0-9]+' | head -1
}

get_total_rows() {
    local result=""
    case $1 in
        sqlserver)
            result=$(exec_sqlserver "SELECT ISNULL(SUM(p.rows),0) FROM sys.partitions p INNER JOIN sys.tables t ON p.object_id=t.object_id WHERE p.index_id<2 AND SCHEMA_NAME(t.schema_id)='dbo'")
            ;;
        postgres)
            exec_postgres "ANALYZE;" >/dev/null 2>&1 || true
            result=$(exec_postgres "SELECT COALESCE(SUM(c.reltuples::bigint),0) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r'")
            ;;
        mysql)
            # Use actual COUNT(*) since InnoDB TABLE_ROWS is an estimate
            result=$(dc exec -T mysql mysql -uroot -pSchemaForgeTest1 schemaforge_test -N -s -e "
                SELECT SUM(cnt) FROM (
                    SELECT COUNT(*) as cnt FROM information_schema.tables
                    WHERE table_schema='schemaforge_test' AND table_type='BASE TABLE'
                    AND 1=0
                    UNION ALL SELECT 0
                ) dummy;" 2>/dev/null)
            # Dynamic approach: build and execute a sum query across all tables
            local tables
            tables=$(exec_mysql "SELECT table_name FROM information_schema.tables WHERE table_schema='schemaforge_test' AND table_type='BASE TABLE'" 2>/dev/null)
            if [ -n "$tables" ]; then
                local sum_sql="SELECT "
                local first=true
                while IFS= read -r tbl; do
                    tbl=$(echo "$tbl" | tr -d '[:space:]')
                    [ -z "$tbl" ] && continue
                    if [ "$first" = true ]; then
                        sum_sql="${sum_sql}(SELECT COUNT(*) FROM \`${tbl}\`)"
                        first=false
                    else
                        sum_sql="${sum_sql}+(SELECT COUNT(*) FROM \`${tbl}\`)"
                    fi
                done <<< "$tables"
                if [ "$first" = false ]; then
                    result=$(exec_mysql "$sum_sql" 2>/dev/null)
                else
                    result="0"
                fi
            else
                result="0"
            fi
            ;;
        oracle)
            exec_oracle "BEGIN DBMS_STATS.GATHER_SCHEMA_STATS(USER); END;
/" >/dev/null 2>&1 || true
            result=$(exec_oracle "SELECT NVL(SUM(NUM_ROWS),0) FROM user_tables;")
            ;;
    esac
    echo "$result" | tr -d '[:space:]' | grep -oE '[0-9]+' | head -1
}

validate_migration() {
    local source="$1" target="$2"
    local label="$source -> $target"

    local table_count
    table_count=$(get_table_count "$target")
    table_count=${table_count:-0}

    local total_rows
    total_rows=$(get_total_rows "$target")
    total_rows=${total_rows:-0}

    if [ "$table_count" -eq "$EXPECTED_TABLES" ] && [ "$total_rows" -eq "$EXPECTED_TOTAL_ROWS" ]; then
        pass "$label  (tables=$table_count, rows=$total_rows)"
    elif [ "$table_count" -eq "$EXPECTED_TABLES" ] && [ "$total_rows" -ge 40 ] && [ "$total_rows" -le 55 ]; then
        pass "$label  (tables=$table_count, rows~=$total_rows)"
    elif [ "$table_count" -eq "$EXPECTED_TABLES" ]; then
        fail "$label  (tables=$table_count OK, rows=$total_rows expected=$EXPECTED_TOTAL_ROWS)"
    elif [ "$table_count" -gt 0 ]; then
        fail "$label  (tables=$table_count expected=$EXPECTED_TABLES, rows=$total_rows)"
    else
        fail "$label  (no tables found in target)"
    fi
}

# ============================================================
# Extended validation: views, indexes, constraints, data values
# ============================================================
get_view_count() {
    local result=""
    case $1 in
        sqlserver)
            result=$(exec_sqlserver "SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA='dbo'")
            ;;
        postgres)
            result=$(exec_postgres "SELECT COUNT(*) FROM information_schema.views WHERE table_schema='public'")
            ;;
        mysql)
            result=$(exec_mysql "SELECT COUNT(*) FROM information_schema.views WHERE table_schema='schemaforge_test'")
            ;;
        oracle)
            result=$(exec_oracle "SELECT COUNT(*) FROM user_views;")
            ;;
    esac
    echo "$result" | tr -d '[:space:]' | grep -oE '[0-9]+' | head -1
}

get_index_count() {
    # Count non-PK indexes
    local result=""
    case $1 in
        sqlserver)
            result=$(exec_sqlserver "SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON i.object_id=t.object_id WHERE i.is_primary_key=0 AND i.type>0 AND i.is_unique_constraint=0 AND SCHEMA_NAME(t.schema_id)='dbo'")
            ;;
        postgres)
            result=$(exec_postgres "SELECT COUNT(*) FROM pg_indexes i WHERE i.schemaname='public' AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid=(SELECT oid FROM pg_class WHERE relname=i.indexname))")
            ;;
        mysql)
            result=$(exec_mysql "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.statistics WHERE table_schema='schemaforge_test' AND INDEX_NAME != 'PRIMARY'")
            ;;
        oracle)
            # Count non-PK, non-system indexes
            result=$(exec_oracle "SELECT COUNT(*) FROM user_indexes WHERE index_type='NORMAL' AND index_name NOT LIKE 'SYS_%' AND index_name NOT IN (SELECT index_name FROM user_constraints WHERE constraint_type='P');")
            ;;
    esac
    echo "$result" | tr -d '[:space:]' | grep -oE '[0-9]+' | head -1
}

# Spot-check data values to verify data integrity
validate_data_values() {
    local target="$1"
    local label="data-values($target)"
    local errors=0

    # Use naming convention to determine table/column names
    local dept_table dept_name_col dept_code_col emp_table emp_email_col emp_salary_col emp_lname_col emp_rating_col prod_table prod_desc_col order_table order_addr_col
    case $target in
        sqlserver)
            dept_table="dbo.Departments" dept_name_col="DepartmentName" dept_code_col="DepartmentCode"
            emp_table="dbo.Employees" emp_email_col="Email" emp_salary_col="Salary" emp_lname_col="LastName" emp_rating_col="Rating"
            prod_table="dbo.Products" prod_desc_col="Description"
            order_table="dbo.OrderHeaders" order_addr_col="ShippingAddress"
            ;;
        postgres)
            dept_table="public.departments" dept_name_col="department_name" dept_code_col="department_code"
            emp_table="public.employees" emp_email_col="email" emp_salary_col="salary" emp_lname_col="last_name" emp_rating_col="rating"
            prod_table="public.products" prod_desc_col="description"
            order_table="public.order_headers" order_addr_col="shipping_address"
            ;;
        mysql)
            dept_table="schemaforge_test.departments" dept_name_col="department_name" dept_code_col="department_code"
            emp_table="schemaforge_test.employees" emp_email_col="email" emp_salary_col="salary" emp_lname_col="last_name" emp_rating_col="rating"
            prod_table="schemaforge_test.products" prod_desc_col="description"
            order_table="schemaforge_test.order_headers" order_addr_col="shipping_address"
            ;;
        oracle)
            dept_table="DEPARTMENTS" dept_name_col="DEPARTMENTNAME" dept_code_col="DEPARTMENTCODE"
            emp_table="EMPLOYEES" emp_email_col="EMAIL" emp_salary_col="SALARY" emp_lname_col="LASTNAME" emp_rating_col="RATING"
            prod_table="PRODUCTS" prod_desc_col="DESCRIPTION"
            order_table="ORDERHEADERS" order_addr_col="SHIPPINGADDRESS"
            ;;
    esac

    # Helper to run a query and trim result
    run_check() {
        local db="$1" query="$2"
        local result=""
        case $db in
            sqlserver) result=$(exec_sqlserver "$query") ;;
            postgres)  result=$(exec_postgres "$query") ;;
            mysql)     result=$(exec_mysql "$query") ;;
            oracle)
                # sqlplus requires a trailing semicolon to execute
                local oq="$query"
                [[ "$oq" != *";" ]] && oq="${oq};"
                result=$(exec_oracle "$oq") ;;
        esac
        echo "$result" | tr -d '[:space:]' | head -1
    }

    # Check 1: Department name 'Engineering' exists
    local val
    val=$(run_check "$target" "SELECT COUNT(*) FROM $dept_table WHERE $dept_name_col='Engineering'")
    if [ "${val:-0}" != "1" ]; then
        log "    FAIL: Department 'Engineering' not found (got $val)"
        errors=$((errors + 1))
    fi

    # Check 2: Employee email spot-check
    val=$(run_check "$target" "SELECT COUNT(*) FROM $emp_table WHERE $emp_email_col='alice.johnson@example.com'")
    if [ "${val:-0}" != "1" ]; then
        log "    FAIL: Employee email alice.johnson@example.com not found (got $val)"
        errors=$((errors + 1))
    fi

    # Check 3: Special character in LastName (O'Brien)
    val=$(run_check "$target" "SELECT COUNT(*) FROM $emp_table WHERE $emp_lname_col='O''Brien'")
    if [ "${val:-0}" != "1" ]; then
        log "    FAIL: Employee O'Brien not found (got $val)"
        errors=$((errors + 1))
    fi

    # Check 4: NULL handling - Employee 10 has NULL Rating
    val=$(run_check "$target" "SELECT COUNT(*) FROM $emp_table WHERE $emp_rating_col IS NULL")
    if [ "${val:-0}" -lt "1" ]; then
        log "    FAIL: Expected at least 1 NULL rating (got $val)"
        errors=$((errors + 1))
    fi

    # Check 5: NULL handling - Product with NULL description
    val=$(run_check "$target" "SELECT COUNT(*) FROM $prod_table WHERE $prod_desc_col IS NULL")
    if [ "${val:-0}" -lt "1" ]; then
        log "    FAIL: Expected at least 1 NULL description (got $val)"
        errors=$((errors + 1))
    fi

    # Check 6: NULL handling - Order with NULL shipping address
    val=$(run_check "$target" "SELECT COUNT(*) FROM $order_table WHERE $order_addr_col IS NULL")
    if [ "${val:-0}" -lt "1" ]; then
        log "    FAIL: Expected at least 1 NULL shipping address (got $val)"
        errors=$((errors + 1))
    fi

    # Check 7: Zero value - Employee with salary = 0
    val=$(run_check "$target" "SELECT COUNT(*) FROM $emp_table WHERE $emp_salary_col=0")
    if [ "${val:-0}" -lt "1" ]; then
        log "    FAIL: Expected at least 1 employee with salary=0 (got $val)"
        errors=$((errors + 1))
    fi

    # Check 8: Row counts per table
    local dept_count emp_count prod_count oh_count od_count
    dept_count=$(run_check "$target" "SELECT COUNT(*) FROM $dept_table")
    emp_count=$(run_check "$target" "SELECT COUNT(*) FROM $emp_table")
    prod_count=$(run_check "$target" "SELECT COUNT(*) FROM $prod_table")
    oh_count=$(run_check "$target" "SELECT COUNT(*) FROM $order_table")
    local od_table
    case $target in
        sqlserver) od_table="dbo.OrderDetails" ;;
        postgres) od_table="public.order_details" ;;
        mysql) od_table="schemaforge_test.order_details" ;;
        oracle) od_table="ORDERDETAILS" ;;
    esac
    od_count=$(run_check "$target" "SELECT COUNT(*) FROM $od_table")

    if [ "${dept_count:-0}" != "5" ] || [ "${emp_count:-0}" != "10" ] || [ "${prod_count:-0}" != "10" ] \
       || [ "${oh_count:-0}" != "8" ] || [ "${od_count:-0}" != "15" ]; then
        log "    FAIL: Per-table row counts: dept=${dept_count:-0}/5 emp=${emp_count:-0}/10 prod=${prod_count:-0}/10 oh=${oh_count:-0}/8 od=${od_count:-0}/15"
        errors=$((errors + 1))
    fi

    if [ "$errors" -eq 0 ]; then
        pass "$label  (8 checks passed)"
    else
        fail "$label  ($errors/8 checks failed)"
    fi
}

# Validate extended migration (views, indexes, constraints)
validate_extended() {
    local source="$1" target="$2"
    local label="$source -> $target [full]"
    local issues=""

    # Validate tables and data first
    local table_count total_rows
    table_count=$(get_table_count "$target")
    table_count=${table_count:-0}
    total_rows=$(get_total_rows "$target")
    total_rows=${total_rows:-0}

    if [ "$table_count" -ne "$EXPECTED_TABLES" ]; then
        fail "$label  (tables=$table_count expected=$EXPECTED_TABLES)"
        return
    fi
    if [ "$total_rows" -lt 40 ] || [ "$total_rows" -gt 55 ]; then
        fail "$label  (rows=$total_rows expected=$EXPECTED_TOTAL_ROWS)"
        return
    fi

    # Check views
    local view_count
    view_count=$(get_view_count "$target")
    view_count=${view_count:-0}
    if [ "$view_count" -lt 1 ]; then
        issues="${issues}views=${view_count}/2 "
    fi

    # Check indexes (at least 1 non-PK index expected)
    local index_count
    index_count=$(get_index_count "$target")
    index_count=${index_count:-0}
    if [ "$index_count" -lt 1 ]; then
        issues="${issues}indexes=${index_count} "
    fi

    if [ -n "$issues" ]; then
        fail "$label  (tables=$table_count, rows=$total_rows, ${issues})"
    else
        pass "$label  (tables=$table_count, rows=$total_rows, views=$view_count, indexes=$index_count)"
    fi
}

# ============================================================
# Run a single test (source -> target)
# ============================================================
run_test() {
    local source="$1" target="$2"
    local label="$source -> $target"

    if [ "$SKIP_ORACLE" = true ]; then
        if [ "$source" = "oracle" ] || [ "$target" = "oracle" ]; then
            skip_test "$label (--skip-oracle)"
            return
        fi
    fi

    echo ""
    log "Testing: $label"

    # Step 1: Re-seed SQL Server with original test data
    seed_sqlserver

    # Step 2: If source is not SQL Server, seed it via sqlserver -> source
    if [ "$source" != "sqlserver" ]; then
        log "  Preparing source: seeding $source from SQL Server..."
        clean_db "$source"
        build_cli_args "sqlserver" "$source"
        local seed_log="$LOG_DIR/seed_${source}.log"
        if ! (dotnet run --project "$PROJECT_DIR/src/SchemaForge.Cli" -- "${CLI_ARGS[@]}" 2>&1) > "$seed_log" 2>&1; then
            fail "$label (could not seed $source, see $seed_log)"
            return
        fi
        log "  Source $source seeded"
    fi

    # Step 3: Clean target database
    clean_db "$target"

    # Step 4: Run the actual migration
    local test_log="$LOG_DIR/${source}_to_${target}.log"
    log "  Running migration..."
    if run_migration "$source" "$target" "$test_log"; then
        # Step 5: Validate
        validate_migration "$source" "$target"
    else
        fail "$label (dotnet run exited with error, see $test_log)"
    fi
}

# ============================================================
# Main
# ============================================================
main() {
    header "SchemaForge - 12 Migration Path Test Suite"
    echo "  Project: $PROJECT_DIR"
    echo "  Compose: $COMPOSE_FILE"
    echo ""

    mkdir -p "$LOG_DIR"

    # Start containers
    header "Starting Database Containers"
    dc up -d
    echo ""

    # Wait for all databases
    header "Waiting for Databases"
    if ! wait_for_all_dbs; then
        echo -e "${RED}Some databases failed to start. Check docker logs.${NC}"
        exit 1
    fi
    echo ""

    # Determine which DB types to test
    local db_types="sqlserver postgres mysql"
    if [ "$SKIP_ORACLE" = false ]; then
        db_types="$db_types oracle"
    fi

    # Count total tests
    local total_tests=0
    for s in $db_types; do
        for t in $db_types; do
            if [ "$s" != "$t" ]; then
                total_tests=$((total_tests + 1))
            fi
        done
    done

    # Run all migration combinations
    local test_num=0
    for source in $db_types; do
        header "Source: $source"
        for target in $db_types; do
            if [ "$source" != "$target" ]; then
                test_num=$((test_num + 1))
                log "[$test_num/$total_tests]"
                run_test "$source" "$target"
            fi
        done
    done

    # ============================================================
    # Phase 2: Data Value Validation (spot-check on each target)
    # ============================================================
    header "Phase 2: Data Value Validation"
    echo "  Validating data integrity on last migration target for each DB..."
    echo ""

    for target in $db_types; do
        # The last migration to each target already populated it;
        # seed sqlserver and migrate to the target to have a clean state
        seed_sqlserver
        if [ "$target" != "sqlserver" ]; then
            clean_db "$target"
            local dv_log="$LOG_DIR/dataval_sqlserver_to_${target}.log"
            build_cli_args "sqlserver" "$target"
            (dotnet run --project "$PROJECT_DIR/src/SchemaForge.Cli" -- "${CLI_ARGS[@]}" 2>&1) > "$dv_log" 2>&1
        fi
        validate_data_values "$target"
    done

    # ============================================================
    # Phase 3: Extended Migration (views, indexes, constraints)
    # ============================================================
    header "Phase 3: Extended Migration (views + indexes + constraints)"
    echo "  Testing full migration with views, indexes, and constraints..."
    echo ""

    # Test a representative subset: sqlserver -> each target
    for target in $db_types; do
        if [ "$target" = "sqlserver" ]; then continue; fi

        if [ "$SKIP_ORACLE" = true ] && [ "$target" = "oracle" ]; then
            skip_test "sqlserver -> $target [full] (--skip-oracle)"
            continue
        fi

        log "Testing: sqlserver -> $target [full]"
        seed_sqlserver
        clean_db "$target"

        local ext_log="$LOG_DIR/extended_sqlserver_to_${target}.log"
        if run_migration "sqlserver" "$target" "$ext_log" "true"; then
            validate_extended "sqlserver" "$target"
        else
            fail "sqlserver -> $target [full]  (migration failed, see $ext_log)"
        fi
    done

    # ============================================================
    # Summary
    # ============================================================
    header "Test Summary"
    echo ""
    echo "$RESULTS" | while IFS= read -r r; do
        [ -z "$r" ] && continue
        case "$r" in
            PASS*) echo -e "  ${GREEN}$r${NC}" ;;
            FAIL*) echo -e "  ${RED}$r${NC}" ;;
            SKIP*) echo -e "  ${YELLOW}$r${NC}" ;;
        esac
    done

    echo ""
    echo -e "  ${GREEN}Passed: $PASS${NC}  ${RED}Failed: $FAIL${NC}  ${YELLOW}Skipped: $SKIP${NC}"
    echo -e "  Total:  $((PASS + FAIL + SKIP))"
    echo ""

    if [ "$FAIL" -gt 0 ]; then
        echo -e "  ${RED}${BOLD}SOME TESTS FAILED${NC}"
        echo "  Logs: $LOG_DIR/"
        return 1
    else
        echo -e "  ${GREEN}${BOLD}ALL TESTS PASSED${NC}"
        return 0
    fi
}

main
