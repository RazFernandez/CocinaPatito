#!/usr/bin/env bash
# =============================================================================
# CocinaPatito / SplitBill — development container bootstrap.
#
# Run by Vagrant as root (see ../Vagrantfile). Idempotent: running it again only does what is
# missing, re-applies passwords from db.env and rebuilds the solution.
#
# Steps
#   0. Load and validate credentials from provision/db.env
#   1. Base system packages
#   2. .NET 10 SDK
#   3. PostgreSQL 18 (official PGDG repository)
#   4. PostgreSQL configuration and start
#   5. Database, roles and schema (SQL/0001, SQL/0002)
#   6. Project libraries: NuGet restore and build
#
# Manual run inside the container:  sudo bash /vagrant/provision/bootstrap.sh
# =============================================================================

set -euo pipefail

readonly PROJECT_DIR="/vagrant"
readonly ENV_FILE="${PROJECT_DIR}/provision/db.env"
readonly SOLUTION="SplitBill.slnx"
readonly DOTNET_CHANNEL="10.0"
readonly DOTNET_DIR="/usr/share/dotnet"
readonly PG_VERSION="18"
readonly PG_CLUSTER="main"
readonly APP_ROLE="splitbill_app"            # fixed by SQL/0002_app_role.sql
readonly DEV_USER="vagrant"
readonly ARTIFACTS_DIR="/home/${DEV_USER}/.artifacts/cocinapatito"

step() { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
info() { printf '    %s\n' "$*"; }
fail() { printf '\033[1;31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

# -----------------------------------------------------------------------------
step "0/6 Loading database credentials from provision/db.env"
# -----------------------------------------------------------------------------
if [[ ! -f "${ENV_FILE}" ]]; then
  fail "provision/db.env not found.
       Copy provision/db.env.example to provision/db.env, edit the user and passwords,
       then run: vagrant provision"
fi

# Strip Windows line endings in case the file was edited with a Windows editor.
set -a
# shellcheck disable=SC1090
source <(tr -d '\r' < "${ENV_FILE}")
set +a

for var in DB_NAME DB_OWNER_USER DB_OWNER_PASSWORD DB_APP_PASSWORD; do
  [[ -n "${!var:-}" ]] || fail "${var} is empty in provision/db.env"
done

identifier_pattern='^[a-z_][a-z0-9_]{0,62}$'
[[ "${DB_NAME}" =~ ${identifier_pattern} ]]       || fail "DB_NAME must match ${identifier_pattern}"
[[ "${DB_OWNER_USER}" =~ ${identifier_pattern} ]] || fail "DB_OWNER_USER must match ${identifier_pattern}"
[[ "${DB_OWNER_USER}" != "${APP_ROLE}" && "${DB_OWNER_USER}" != "postgres" ]] \
  || fail "DB_OWNER_USER cannot be '${APP_ROLE}' or 'postgres'"

for var in DB_OWNER_PASSWORD DB_APP_PASSWORD; do
  value="${!var}"
  [[ "${value}" != change-me* ]] || fail "${var} still has the placeholder value. Edit provision/db.env"
  (( ${#value} >= 12 )) || fail "${var} in provision/db.env must have at least 12 characters"
done
unset value
info "database=${DB_NAME} owner=${DB_OWNER_USER} app_role=${APP_ROLE} (passwords not shown)"

# -----------------------------------------------------------------------------
step "1/6 Installing base system packages"
# -----------------------------------------------------------------------------
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq --no-install-recommends \
  ca-certificates curl gnupg lsb-release git unzip nano \
  libc6 libgcc-s1 libstdc++6 zlib1g libicu74 libssl3t64 libunwind8 liblttng-ust1t64 \
  >/dev/null
info "done"

# -----------------------------------------------------------------------------
step "2/6 Installing the .NET ${DOTNET_CHANNEL} SDK"
# -----------------------------------------------------------------------------
if [[ -x "${DOTNET_DIR}/dotnet" ]] && "${DOTNET_DIR}/dotnet" --list-sdks | grep -q "^${DOTNET_CHANNEL%%.*}\."; then
  info "already installed: $("${DOTNET_DIR}/dotnet" --list-sdks | grep "^${DOTNET_CHANNEL%%.*}\." | tail -1)"
else
  # Official Microsoft installer script; installs the latest SDK of the channel.
  installer="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${installer}"
  bash "${installer}" --channel "${DOTNET_CHANNEL}" --install-dir "${DOTNET_DIR}" --no-path
  rm -f "${installer}"
  ln -sf "${DOTNET_DIR}/dotnet" /usr/local/bin/dotnet
fi

# Environment for every login shell (vagrant ssh) and for the restore below.
#  - Build output goes to the container's own folder instead of bin/ and obj/ inside /vagrant,
#    so container builds never clash with builds made on Windows over the same files.
cat > /etc/profile.d/dotnet.sh <<EOF
export DOTNET_ROOT=${DOTNET_DIR}
export PATH="\$PATH:${DOTNET_DIR}:\$HOME/.dotnet/tools"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export UseArtifactsOutput=true
export ArtifactsPath=${ARTIFACTS_DIR}
EOF
chmod 0644 /etc/profile.d/dotnet.sh
# shellcheck disable=SC1091
source /etc/profile.d/dotnet.sh
info "$(dotnet --version) at ${DOTNET_DIR}"

# -----------------------------------------------------------------------------
step "3/6 Installing PostgreSQL ${PG_VERSION}"
# -----------------------------------------------------------------------------
if dpkg -s "postgresql-${PG_VERSION}" >/dev/null 2>&1; then
  info "already installed"
else
  apt-get install -y -qq --no-install-recommends postgresql-common >/dev/null
  # Adds the official apt.postgresql.org repository and its signing key.
  /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y >/dev/null
  apt-get install -y -qq --no-install-recommends "postgresql-${PG_VERSION}" >/dev/null
fi
info "$(/usr/lib/postgresql/${PG_VERSION}/bin/postgres --version)"

# -----------------------------------------------------------------------------
step "4/6 Configuring and starting PostgreSQL"
# -----------------------------------------------------------------------------
pg_conf_dir="/etc/postgresql/${PG_VERSION}/${PG_CLUSTER}"

# Listen on all container interfaces so the port published on Windows' 127.0.0.1:5432 works.
# The Vagrantfile publishes it on 127.0.0.1 only: nothing outside this computer can connect.
cat > "${pg_conf_dir}/conf.d/10-cocinapatito.conf" <<'EOF'
listen_addresses = '*'
password_encryption = 'scram-sha-256'
EOF

hba_marker="# cocinapatito-dev"
if ! grep -q "${hba_marker}" "${pg_conf_dir}/pg_hba.conf"; then
  cat >> "${pg_conf_dir}/pg_hba.conf" <<EOF
${hba_marker}: password logins from the Docker network (published only on the host's 127.0.0.1)
host    all    all    0.0.0.0/0    scram-sha-256
EOF
fi

if pg_lsclusters --no-header | awk '{print $4}' | grep -q online; then
  pg_ctlcluster "${PG_VERSION}" "${PG_CLUSTER}" reload
else
  pg_ctlcluster "${PG_VERSION}" "${PG_CLUSTER}" start
fi
info "$(pg_lsclusters --no-header)"

# -----------------------------------------------------------------------------
step "5/6 Creating database, roles and schema"
# -----------------------------------------------------------------------------
# sudo resets the environment; keep only the two passwords so \getenv can read them.
psql_admin() {
  sudo -u postgres --preserve-env=DB_OWNER_PASSWORD,DB_APP_PASSWORD \
    psql -X -q -v ON_ERROR_STOP=1 "$@"
}

# Every value reaches SQL as a psql variable: :'x' inserts a quoted literal and :"x" a quoted
# identifier, so nothing from db.env is ever concatenated into the SQL text. Passwords travel as
# environment variables read with \getenv: they never appear on a command line.
exists() { psql_admin -tA "$@" | grep -qx 1; }

if ! exists -v name="${DB_OWNER_USER}" <<< "SELECT 1 FROM pg_roles WHERE rolname = :'name';"; then
  psql_admin -v owner="${DB_OWNER_USER}" <<'SQL'
CREATE ROLE :"owner" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
SQL
  info "role ${DB_OWNER_USER} created"
fi
psql_admin -v owner="${DB_OWNER_USER}" <<'SQL'
\getenv owner_password DB_OWNER_PASSWORD
ALTER ROLE :"owner" PASSWORD :'owner_password';
SQL

# Database owned by the owner role.
if ! exists -v name="${DB_NAME}" <<< "SELECT 1 FROM pg_database WHERE datname = :'name';"; then
  psql_admin -v db="${DB_NAME}" -v owner="${DB_OWNER_USER}" <<'SQL'
CREATE DATABASE :"db" OWNER :"owner" ENCODING 'UTF8' TEMPLATE template0;
SQL
  info "database ${DB_NAME} created"
fi
psql_admin -d "${DB_NAME}" -c "REVOKE CREATE ON SCHEMA public FROM PUBLIC;"

# Schema: run as the owner so it owns every table. Skipped once the schema exists.
if ! exists -d "${DB_NAME}" <<< "SELECT 1 FROM pg_namespace WHERE nspname = 'splitbill';"; then
  PGPASSWORD="${DB_OWNER_PASSWORD}" psql -X -q -v ON_ERROR_STOP=1 \
    -h 127.0.0.1 -U "${DB_OWNER_USER}" -d "${DB_NAME}" -f "${PROJECT_DIR}/SQL/0001_initial_schema.sql"
  info "SQL/0001_initial_schema.sql applied"
else
  info "schema splitbill already exists (0001 skipped)"
fi

# Least-privilege application role and grants (idempotent), then its password.
psql_admin -d "${DB_NAME}" -f "${PROJECT_DIR}/SQL/0002_app_role.sql"
psql_admin -d "${DB_NAME}" <<'SQL'
\getenv app_password DB_APP_PASSWORD
ALTER ROLE splitbill_app PASSWORD :'app_password';
SQL
info "SQL/0002_app_role.sql applied; ${APP_ROLE} password set"

table_count="$(psql_admin -d "${DB_NAME}" -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'splitbill'")"
info "tables in schema splitbill: ${table_count}"

# -----------------------------------------------------------------------------
step "6/6 Restoring project libraries (NuGet) and building"
# -----------------------------------------------------------------------------
# Runs as the vagrant user so packages land in /home/vagrant/.nuget and build output in
# ${ARTIFACTS_DIR}. Versions come from Directory.Packages.props and the lock files.
sudo -u "${DEV_USER}" -H bash -lc "cd '${PROJECT_DIR}' && dotnet restore '${SOLUTION}' && dotnet build '${SOLUTION}' --no-restore -v q"

step "Done"
cat <<EOF
    Open a shell:        vagrant ssh
    Run the tests:       cd /vagrant && dotnet test
    Database from Windows (DBeaver, pgAdmin, psql):
        host 127.0.0.1  port 5433  database ${DB_NAME}
        owner user ${DB_OWNER_USER}  /  app user ${APP_ROLE}  (passwords in provision/db.env)
EOF
