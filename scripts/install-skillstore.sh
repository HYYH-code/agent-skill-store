#!/usr/bin/env bash
set -euo pipefail

REPOSITORY="${AGENT_SKILL_STORE_REPOSITORY:-agent-skill-store/agent-skill-store}"
INSTALL_DIR="${INSTALL_DIR:-${HOME}/.local/bin}"
REQUESTED_VERSION="${1:-latest}"
BINARY_NAME="skillstore"

detect_rid() {
    local os arch
    os="$(uname -s)"
    arch="$(uname -m)"
    case "$os" in
        Linux) os="linux" ;;
        Darwin) os="osx" ;;
        *) echo "Unsupported operating system: $os" >&2; exit 1 ;;
    esac
    case "$arch" in
        x86_64|amd64) arch="x64" ;;
        aarch64|arm64) arch="arm64" ;;
        *) echo "Unsupported architecture: $arch" >&2; exit 1 ;;
    esac
    if [ "$os-$arch" = "osx-x64" ]; then
        echo "The current release does not provide an osx-x64 binary." >&2
        exit 1
    fi
    printf '%s-%s\n' "$os" "$arch"
}

resolve_tag() {
    if [ "$REQUESTED_VERSION" != "latest" ]; then
        case "$REQUESTED_VERSION" in
            v*) printf '%s\n' "$REQUESTED_VERSION" ;;
            *) printf 'v%s\n' "$REQUESTED_VERSION" ;;
        esac
        return
    fi
    curl -fsSL "https://api.github.com/repos/${REPOSITORY}/releases/latest" |
        sed -n 's/.*"tag_name":[[:space:]]*"\([^"]*\)".*/\1/p' |
        head -n 1
}

RID="$(detect_rid)"
TAG="$(resolve_tag)"
if [ -z "$TAG" ]; then
    echo "Could not determine the release tag." >&2
    exit 1
fi
VERSION="${TAG#v}"
ARCHIVE_NAME="agent-skill-store-${VERSION}-${RID}.tar.gz"
RELEASE_BASE_URL="https://github.com/${REPOSITORY}/releases/download/${TAG}"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

echo "Installing ${BINARY_NAME} ${VERSION} for ${RID}..."
curl -fsSL -o "${TMP_DIR}/${ARCHIVE_NAME}" "${RELEASE_BASE_URL}/${ARCHIVE_NAME}"
curl -fsSL -o "${TMP_DIR}/SHA256SUMS" "${RELEASE_BASE_URL}/SHA256SUMS"
(
    cd "$TMP_DIR"
    expected_line="$(grep -E "[[:space:]]\*?${ARCHIVE_NAME}$" SHA256SUMS || true)"
    if [ -z "$expected_line" ]; then
        echo "SHA256SUMS does not contain ${ARCHIVE_NAME}." >&2
        exit 1
    fi
    expected="$(printf '%s\n' "$expected_line" | awk '{print $1}')"
    if command -v sha256sum >/dev/null 2>&1; then
        actual="$(sha256sum "$ARCHIVE_NAME" | awk '{print $1}')"
    else
        actual="$(shasum -a 256 "$ARCHIVE_NAME" | awk '{print $1}')"
    fi
    [ "$expected" = "$actual" ] || { echo "Checksum mismatch for ${ARCHIVE_NAME}." >&2; exit 1; }
)

tar -xzf "${TMP_DIR}/${ARCHIVE_NAME}" -C "$TMP_DIR"
[ -f "${TMP_DIR}/${BINARY_NAME}" ] || { echo "Archive does not contain ${BINARY_NAME}." >&2; exit 1; }
mkdir -p "$INSTALL_DIR"
install -m 0755 "${TMP_DIR}/${BINARY_NAME}" "${INSTALL_DIR}/${BINARY_NAME}"

echo "Installed ${BINARY_NAME} to ${INSTALL_DIR}/${BINARY_NAME}"
case ":${PATH}:" in
    *":${INSTALL_DIR}:"*) ;;
    *) echo "Add ${INSTALL_DIR} to PATH before running ${BINARY_NAME}." ;;
esac
