#!/usr/bin/env bash
# install-debian.sh — Saga Importer Linux Daemon installer
#
# Turns a fresh Debian 13 "trixie" (or later) server into a Saga Importer
# appliance: Samba inbox share + headless .NET daemon + systemd service.
#
# Usage:
#   sudo bash install-debian.sh
#
# Or pre-set variables to run non-interactively:
#   sudo SAGA_BASE_URL=http://192.168.1.10:8000 \
#        SAGA_API_TOKEN=my-token \
#        SMB_USER=smbinbox \
#        SMB_PASSWORD=s3cret \
#        bash install-debian.sh
#
# Re-running this script is safe (idempotent).

set -euo pipefail

# ---------------------------------------------------------------------------
# Error trap: report the failing line number and exit cleanly.
# ---------------------------------------------------------------------------
trap 'echo "[ERROR] install-debian.sh failed at line ${LINENO}. Aborting." >&2' ERR

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
DOTNET_VERSION="10.0"
DAEMON_PROJECT="src/SagaImporter.Daemon"
DAEMON_BINARY_NAME="SagaImporter.Daemon"
SERVICE_NAME="saga-importer"
CONFIG_DIR="/etc/saga-importer"
CONFIG_FILE="${CONFIG_DIR}/settings.json"
LOG_DIR="/var/log/saga-importer"
SMB_CONF="/etc/samba/smb.conf"
SMB_MARKER_BEGIN="# --- BEGIN saga-importer managed block ---"
SMB_MARKER_END="# --- END saga-importer managed block ---"

# ---------------------------------------------------------------------------
# Defaults (can be overridden via environment)
# ---------------------------------------------------------------------------
: "${INBOX_DIR:=/srv/saga/inbox}"
: "${APP_DIR:=/opt/saga-importer}"
: "${SERVICE_USER:=saga-importer}"
: "${SMB_GUEST:=0}"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
info()  { echo "[INFO ] $*"; }
warn()  { echo "[WARN ] $*" >&2; }
error() { echo "[ERROR] $*" >&2; exit 1; }

require_root() {
    if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
        error "This script must be run as root. Try: sudo bash $0"
    fi
}

apt_retry() {
    local attempt max_attempts=3 delay=10
    for (( attempt=1; attempt<=max_attempts; attempt++ )); do
        if "$@"; then
            return 0
        fi
        warn "apt command failed (attempt ${attempt}/${max_attempts}), retrying in ${delay}s..."
        sleep "${delay}"
    done
    error "apt command failed after ${max_attempts} attempts: $*"
}

# ---------------------------------------------------------------------------
# Step 0: Checks
# ---------------------------------------------------------------------------
require_root

if [[ ! -f /etc/debian_version ]]; then
    error "This script only supports Debian. Detected OS is not Debian."
fi

DEBIAN_VERSION=$(cat /etc/debian_version)
info "Detected Debian ${DEBIAN_VERSION}."

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(dirname "${SCRIPT_DIR}")"
if [[ ! -f "${REPO_DIR}/${DAEMON_PROJECT}/SagaImporter.Daemon.csproj" ]]; then
    error "Run this script from within the saga-importer repository checkout. Expected: ${REPO_DIR}/${DAEMON_PROJECT}"
fi

# ---------------------------------------------------------------------------
# Step 1: Collect configuration (interactive fallback)
# ---------------------------------------------------------------------------
prompt_if_empty() {
    local var_name="$1" prompt_text="$2" secret="${3:-0}"
    local current_val="${!var_name:-}"
    if [[ -z "${current_val}" ]]; then
        if [[ "${secret}" -eq 1 ]]; then
            read -r -s -p "${prompt_text}: " current_val
            echo
        else
            read -r -p "${prompt_text}: " current_val
        fi
        eval "${var_name}=\${current_val}"
    fi
}

prompt_if_empty SAGA_BASE_URL  "Saga base URL (e.g. http://192.168.1.10:8000)"
prompt_if_empty SAGA_API_TOKEN "Saga API token" 1
prompt_if_empty SMB_USER           "Samba username for the inbox share (e.g. smbinbox)"

if [[ "${SMB_GUEST}" -ne 1 ]]; then
    prompt_if_empty SMB_PASSWORD "Samba password for ${SMB_USER}" 1
fi

# Validate mandatory fields
[[ -z "${SAGA_BASE_URL:-}" ]]  && error "SAGA_BASE_URL is required."
[[ -z "${SAGA_API_TOKEN:-}" ]] && error "SAGA_API_TOKEN is required."
[[ -z "${SMB_USER:-}" ]]           && error "SMB_USER is required."

info "Configuration:"
info "  Inbox dir:      ${INBOX_DIR}"
info "  App dir:        ${APP_DIR}"
info "  Service user:   ${SERVICE_USER}"
info "  Saga URL:   ${SAGA_BASE_URL}"
info "  Samba user:     ${SMB_USER}"
info "  Guest share:    ${SMB_GUEST}"

# ---------------------------------------------------------------------------
# Step 2: Package installation
# ---------------------------------------------------------------------------
info "Updating package lists..."
apt_retry apt-get update -qq

info "Installing required packages..."
apt_retry apt-get install -y -qq \
    samba \
    acl \
    ca-certificates \
    curl

# ---------------------------------------------------------------------------
# Step 3: Install .NET 10 SDK
# ---------------------------------------------------------------------------
DOTNET_INSTALL_DIR="/opt/dotnet"
DOTNET_BIN="${DOTNET_INSTALL_DIR}/dotnet"

if "${DOTNET_BIN}" --list-sdks 2>/dev/null | grep -q "^${DOTNET_VERSION}"; then
    info ".NET ${DOTNET_VERSION} SDK already installed."
else
    info "Installing .NET ${DOTNET_VERSION} SDK via dotnet-install.sh..."
    DOTNET_INSTALL_SCRIPT="$(mktemp)"
    curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "${DOTNET_INSTALL_SCRIPT}"
    chmod +x "${DOTNET_INSTALL_SCRIPT}"
    bash "${DOTNET_INSTALL_SCRIPT}" \
        --channel "${DOTNET_VERSION}" \
        --install-dir "${DOTNET_INSTALL_DIR}" \
        --no-path
    rm -f "${DOTNET_INSTALL_SCRIPT}"
    info ".NET SDK installed to ${DOTNET_INSTALL_DIR}."
fi

export PATH="${DOTNET_INSTALL_DIR}:${PATH}"
"${DOTNET_BIN}" --version

# ---------------------------------------------------------------------------
# Step 4: Create service user and directories
# ---------------------------------------------------------------------------
if ! id -u "${SERVICE_USER}" &>/dev/null; then
    info "Creating system user '${SERVICE_USER}'..."
    useradd --system --no-create-home --shell /usr/sbin/nologin \
        --comment "Saga Importer daemon" "${SERVICE_USER}"
else
    info "System user '${SERVICE_USER}' already exists."
fi

info "Creating inbox directories..."
mkdir -p "${INBOX_DIR}/failed"
chown -R "${SERVICE_USER}:${SERVICE_USER}" "${INBOX_DIR}"
chmod 775 "${INBOX_DIR}"
chmod 775 "${INBOX_DIR}/failed"

mkdir -p "${LOG_DIR}"
chown "${SERVICE_USER}:${SERVICE_USER}" "${LOG_DIR}"
chmod 750 "${LOG_DIR}"

# ---------------------------------------------------------------------------
# Step 5: Build and publish the daemon
# ---------------------------------------------------------------------------
info "Publishing Saga Importer Daemon (self-contained, linux-x64)..."
mkdir -p "${APP_DIR}"

"${DOTNET_BIN}" publish \
    "${REPO_DIR}/${DAEMON_PROJECT}/SagaImporter.Daemon.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -o "${APP_DIR}" \
    --nologo -v quiet

if [[ ! -f "${APP_DIR}/${DAEMON_BINARY_NAME}" ]]; then
    error "Build succeeded but binary not found at ${APP_DIR}/${DAEMON_BINARY_NAME}"
fi

chown root:root "${APP_DIR}/${DAEMON_BINARY_NAME}"
chmod 755 "${APP_DIR}/${DAEMON_BINARY_NAME}"
info "Daemon published to ${APP_DIR}/${DAEMON_BINARY_NAME}."

# ---------------------------------------------------------------------------
# Step 6: Write configuration file
# ---------------------------------------------------------------------------
info "Writing daemon configuration to ${CONFIG_FILE}..."
mkdir -p "${CONFIG_DIR}"

# Write the settings file, injecting base URL and token.
# Token is stored in plaintext — the file is chmod 600 and owned by the
# service user (file-permission protection, documented in the security notes).
cat > "${CONFIG_FILE}" <<JSON
{
  "watchedFolder": "${INBOX_DIR}",
  "baseUrl": "${SAGA_BASE_URL}",
  "encryptedApiToken": "${SAGA_API_TOKEN}",
  "deleteTrigger": 0,
  "statusPollTimeoutMinutes": 10,
  "statusPollIntervalSeconds": 3,
  "rescanIntervalMinutes": 10,
  "failedSubfolderName": "failed",
  "includeExtensions": "",
  "excludeExtensions": ".tmp,.crdownload,.part",
  "stabilityDelaySeconds": 2,
  "startWithWindows": false,
  "smbShareName": "Inbox"
}
JSON

chown "root:${SERVICE_USER}" "${CONFIG_FILE}"
chmod 640 "${CONFIG_FILE}"
info "Configuration written (mode 640, owner root:${SERVICE_USER})."

# ---------------------------------------------------------------------------
# Step 7: Configure Samba
# ---------------------------------------------------------------------------
info "Configuring Samba share '${SMB_SHARE_NAME:-Inbox}'..."
SMB_SHARE_NAME="${SMB_SHARE_NAME:-Inbox}"

# Determine guest line
if [[ "${SMB_GUEST}" -eq 1 ]]; then
    GUEST_LINE="   guest ok = yes"
else
    GUEST_LINE="   valid users = ${SMB_USER}"
fi

# Remove any existing managed block (idempotent update)
if grep -q "${SMB_MARKER_BEGIN}" "${SMB_CONF}" 2>/dev/null; then
    info "Removing existing Samba managed block..."
    # Use a temp file to avoid sed -i portability issues
    python3 -c "
import sys, re
text = open('${SMB_CONF}').read()
pattern = r'${SMB_MARKER_BEGIN}.*?${SMB_MARKER_END}\n?'
text = re.sub(pattern, '', text, flags=re.DOTALL)
open('${SMB_CONF}', 'w').write(text)
"
fi

# Append the new managed block
cat >> "${SMB_CONF}" <<SMBCONF

${SMB_MARKER_BEGIN}
# This block is written by install-debian.sh. Do not edit manually.
[${SMB_SHARE_NAME}]
   comment = Saga Importer inbox
   path = ${INBOX_DIR}
   browseable = yes
   read only = no
   create mask = 0660
   directory mask = 0770
   force user = ${SERVICE_USER}
   force group = ${SERVICE_USER}
${GUEST_LINE}
${SMB_MARKER_END}
SMBCONF

info "Validating Samba configuration..."
if ! testparm -s "${SMB_CONF}" &>/dev/null; then
    error "testparm reported an error in ${SMB_CONF}. Review the file and re-run."
fi

# Create Samba user (skip in guest mode)
if [[ "${SMB_GUEST}" -ne 1 ]]; then
    # Ensure the system user exists for Samba (useradd is idempotent above)
    if ! pdbedit -L 2>/dev/null | grep -q "^${SMB_USER}:"; then
        info "Creating Samba user '${SMB_USER}'..."
        (echo "${SMB_PASSWORD}"; echo "${SMB_PASSWORD}") | smbpasswd -a -s "${SMB_USER}"
    else
        info "Samba user '${SMB_USER}' already registered; updating password..."
        (echo "${SMB_PASSWORD}"; echo "${SMB_PASSWORD}") | smbpasswd -s "${SMB_USER}"
    fi
    # Ensure the Samba user account is enabled
    smbpasswd -e "${SMB_USER}"
fi

info "Restarting Samba..."
systemctl restart smbd nmbd

# ---------------------------------------------------------------------------
# Step 8: Install and start the systemd service
# ---------------------------------------------------------------------------
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
info "Installing systemd unit to ${SERVICE_FILE}..."

cat > "${SERVICE_FILE}" <<UNIT
[Unit]
Description=Saga Importer Daemon
Documentation=https://github.com/mlauf-labs/saga-importer
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
User=${SERVICE_USER}
Group=${SERVICE_USER}
ExecStart=${APP_DIR}/${DAEMON_BINARY_NAME}
Restart=on-failure
RestartSec=5s
Environment=SAGA_SETTINGS_PATH=${CONFIG_FILE}
Environment=SAGA_LOG_DIR=${LOG_DIR}
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=${INBOX_DIR} ${LOG_DIR}
ReadOnlyPaths=${CONFIG_DIR}

[Install]
WantedBy=multi-user.target
UNIT

info "Enabling and starting ${SERVICE_NAME}..."
systemctl daemon-reload
systemctl enable "${SERVICE_NAME}"
systemctl restart "${SERVICE_NAME}"

# ---------------------------------------------------------------------------
# Step 9: Verify
# ---------------------------------------------------------------------------
# Wait briefly for the service to settle
sleep 3

if ! systemctl is-active --quiet "${SERVICE_NAME}"; then
    warn "Service does not appear to be active. Check logs with:"
    warn "  journalctl -u ${SERVICE_NAME} --no-pager -n 50"
    exit 1
fi

# Attempt a health check against Saga (best-effort; may fail if Saga is
# on a different host or not yet reachable from this VM)
if curl -sf --max-time 5 "${SAGA_BASE_URL}/health" &>/dev/null; then
    info "Saga health check: OK."
else
    warn "Saga health check did not respond (${SAGA_BASE_URL}/health)."
    warn "The daemon will keep retrying once Saga becomes reachable."
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
HOSTNAME_SHORT="$(hostname -s)"
echo ""
echo "======================================================================"
echo " Saga Importer Daemon installed successfully!"
echo "======================================================================"
echo ""
echo "  Inbox share:     \\\\${HOSTNAME_SHORT}\\${SMB_SHARE_NAME}"
echo "  Inbox folder:    ${INBOX_DIR}"
echo "  Failed folder:   ${INBOX_DIR}/failed"
echo "  Config:          ${CONFIG_FILE}"
echo "  Logs:            ${LOG_DIR}"
echo "  Binary:          ${APP_DIR}/${DAEMON_BINARY_NAME}"
echo ""
echo "  Service status:  $(systemctl is-active ${SERVICE_NAME})"
echo ""
echo "  Useful commands:"
echo "    journalctl -u ${SERVICE_NAME} -f          # live log"
echo "    systemctl status ${SERVICE_NAME}           # service status"
echo "    systemctl restart ${SERVICE_NAME}          # restart after config change"
echo ""
if [[ "${SMB_GUEST}" -ne 1 ]]; then
    echo "  Connect to the share as user '${SMB_USER}' with the password you provided."
else
    echo "  The share is accessible without a password (guest mode)."
fi
echo ""
echo "Drop files into \\\\${HOSTNAME_SHORT}\\${SMB_SHARE_NAME} to import them into Saga."
