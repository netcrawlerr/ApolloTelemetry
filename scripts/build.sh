#!/usr/bin/env bash

set -e

APP_NAME="ApolloTelemetry"
LOWER_APP_NAME="apolletelemetry"
VERSION="${1:-1.0.0}"
TARGET_FRAMEWORK="net10.0"
RID="linux-x64"

# Directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
AGENT_PROJ="${ROOT_DIR}/src/ApolloTelemetry.Agent/ApolloTelemetry.Agent.csproj"
DASHBOARD_PROJ="${ROOT_DIR}/src/ApolloTelemetry.Dashboard/ApolloTelemetry.Dashboard.csproj"
DIST_DIR="${ROOT_DIR}/dist"

WORK_DIR="$(mktemp -d -t apolletelemetry_build_XXXXXX)"
PUBLISH_AGENT_DIR="${WORK_DIR}/publish_agent"
PUBLISH_DASHBOARD_DIR="${WORK_DIR}/publish_dashboard"

cleanup() {
  rm -rf "${WORK_DIR}"
}
trap cleanup EXIT

echo "=========================================="
echo " Building ${APP_NAME} v${VERSION} for Linux (${RID})"
echo "=========================================="

rm -rf "${DIST_DIR}"
mkdir -p "${DIST_DIR}" "${PUBLISH_AGENT_DIR}" "${PUBLISH_DASHBOARD_DIR}"

echo "[1/5] Publishing ApolloTelemetry.Agent (Worker Service)..."
dotnet publish "${AGENT_PROJ}" \
  -c Release \
  -r "${RID}" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishReadyToRun=true \
  -o "${PUBLISH_AGENT_DIR}"

chmod +x "${PUBLISH_AGENT_DIR}/ApolloTelemetry.Agent"

echo "[2/5] Publishing ApolloTelemetry.Dashboard (Desktop App)..."
dotnet publish "${DASHBOARD_PROJ}" \
  -c Release \
  -r "${RID}" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishReadyToRun=true \
  -o "${PUBLISH_DASHBOARD_DIR}"

chmod +x "${PUBLISH_DASHBOARD_DIR}/ApolloTelemetry.Dashboard"

echo "[3/5] Creating distribution archives (.tar.gz)..."
AGENT_TAR="${LOWER_APP_NAME}-agent-${VERSION}-${RID}.tar.gz"
DASHBOARD_TAR="${LOWER_APP_NAME}-dashboard-${VERSION}-${RID}.tar.gz"

tar -czf "${DIST_DIR}/${AGENT_TAR}" -C "${PUBLISH_AGENT_DIR}" .
echo "  -> Created ${DIST_DIR}/${AGENT_TAR}"

tar -czf "${DIST_DIR}/${DASHBOARD_TAR}" -C "${PUBLISH_DASHBOARD_DIR}" .
echo "  -> Created ${DIST_DIR}/${DASHBOARD_TAR}"

echo "[4/5] Building Debian package (.deb) for Dashboard..."
DEB_ROOT="${WORK_DIR}/deb_root"
DEB_OPT_DIR="${DEB_ROOT}/opt/${LOWER_APP_NAME}-dashboard"
mkdir -p "${DEB_OPT_DIR}"
mkdir -p "${DEB_ROOT}/usr/bin"
mkdir -p "${DEB_ROOT}/usr/share/applications"
mkdir -p "${DEB_ROOT}/usr/share/icons/hicolor/512x512/apps"
mkdir -p "${DEB_ROOT}/DEBIAN"

cp -r "${PUBLISH_DASHBOARD_DIR}/"* "${DEB_OPT_DIR}/"

ln -s "/opt/${LOWER_APP_NAME}-dashboard/ApolloTelemetry.Dashboard" "${DEB_ROOT}/usr/bin/${LOWER_APP_NAME}-dashboard"

ICON_SRC="${ROOT_DIR}/src/ApolloTelemetry.Dashboard/Assets/logo.png"
if [ -f "${ICON_SRC}" ]; then
  cp "${ICON_SRC}" "${DEB_ROOT}/usr/share/icons/hicolor/512x512/apps/${LOWER_APP_NAME}.png"
fi

# Create Desktop entry
cat <<EOF > "${DEB_ROOT}/usr/share/applications/${LOWER_APP_NAME}-dashboard.desktop"
[Desktop Entry]
Name=ApolloTelemetry Dashboard
Comment=Local Server Monitoring Dashboard
Exec=${LOWER_APP_NAME}-dashboard
Icon=${LOWER_APP_NAME}
Terminal=false
Type=Application
Categories=Utility;System;Network;
StartupWMClass=ApolloTelemetry.Dashboard
EOF

cat <<EOF > "${DEB_ROOT}/DEBIAN/control"
Package: ${LOWER_APP_NAME}-dashboard
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Maintainer: ApolloTelemetry Developers <developer@localhost>
Description: Local Server Monitoring Dashboard
 ApolloTelemetry Dashboard is a cross-platform desktop UI built with Avalonia UI
 for tracking local server metrics, performance stats, and active processes.
EOF

if command -v dpkg-deb &> /dev/null; then
  DEB_FILE="${DIST_DIR}/${LOWER_APP_NAME}-dashboard_${VERSION}_amd64.deb"
  dpkg-deb --root-owner-group --build "${DEB_ROOT}" "${DEB_FILE}"
  echo "  -> Created ${DEB_FILE}"
else
  echo "  [Skipped] 'dpkg-deb' is not installed."
fi

echo "[5/5] Building Desktop AppImage..."
APPDIR="${WORK_DIR}/AppDir"
mkdir -p "${APPDIR}/usr/bin"
mkdir -p "${APPDIR}/usr/share/icons/hicolor/512x512/apps"

cp -r "${PUBLISH_DASHBOARD_DIR}/"* "${APPDIR}/usr/bin/"

if [ -f "${ICON_SRC}" ]; then
  cp "${ICON_SRC}" "${APPDIR}/usr/share/icons/hicolor/512x512/apps/${LOWER_APP_NAME}.png"
  cp "${ICON_SRC}" "${APPDIR}/${LOWER_APP_NAME}.png"
  cp "${ICON_SRC}" "${APPDIR}/.DirIcon"
fi

cat <<EOF > "${APPDIR}/${LOWER_APP_NAME}.desktop"
[Desktop Entry]
Name=ApolloTelemetry Dashboard
Comment=Local Server Monitoring Dashboard
Exec=ApolloTelemetry.Dashboard
Icon=${LOWER_APP_NAME}
Terminal=false
Type=Application
Categories=Utility;System;Network;
EOF

cat <<'EOF' > "${APPDIR}/AppRun"
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "${0}")")"
export PATH="${HERE}/usr/bin:${PATH}"
exec "${HERE}/usr/bin/ApolloTelemetry.Dashboard" "$@"
EOF
chmod +x "${APPDIR}/AppRun"

APPIMAGE_FILE="${DIST_DIR}/${APP_NAME}-Dashboard-${VERSION}-x86_64.AppImage"

LOCAL_TOOL="${WORK_DIR}/appimagetool"
if command -v curl &> /dev/null; then
  curl -sSL -o "${LOCAL_TOOL}" "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
elif command -v wget &> /dev/null; then
  wget -q -O "${LOCAL_TOOL}" "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
else
  echo "  [Error] Neither curl nor wget found. Cannot download appimagetool."
  exit 1
fi

chmod +x "${LOCAL_TOOL}"

cd "${WORK_DIR}"
./appimagetool --appimage-extract > /dev/null
EXTRACTED_TOOL="${WORK_DIR}/squashfs-root/AppRun"

ARCH=x86_64 "${EXTRACTED_TOOL}" "${APPDIR}" "${APPIMAGE_FILE}"
echo "  -> Created ${APPIMAGE_FILE}"

echo ""
echo "=========================================="
echo " Build Completed successfully!"
echo " Outputs location: ${DIST_DIR}"
ls -lh "${DIST_DIR}"
echo "=========================================="
