FROM videreproject/mtgosdk:wayland AS tracker-prereqs

ARG WEBVIEW2_RUNTIME_URL=https://go.microsoft.com/fwlink/?linkid=2124701
ENV PATH="/opt/wine/bin:${PATH}"

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
      xauth \
      libegl1 \
      libgl1 \
      libgles2 \
      libgl1-mesa-dri \
    && rm -rf /var/lib/apt/lists/*

USER wine

# Tracker's native WebView2 loader uses the current Visual C++ runtime family.
RUN xvfb-run -a winetricks -q --force vcrun2022

# The current runtime rejects Wine's Windows 7 default, so configure this
# Tracker-specific prefix as Windows 10 before running Microsoft's installer.
RUN xvfb-run -a winetricks -q win10

FROM tracker-prereqs

ARG WEBVIEW2_RUNTIME_URL=https://go.microsoft.com/fwlink/?linkid=2124701

# Install the current x64 Evergreen standalone WebView2 Runtime into the Wine
# prefix. The Microsoft fwlink resolves to the current signed installer.
RUN curl --fail --location --retry 3 \
      --output /tmp/MicrosoftEdgeWebView2RuntimeInstallerX64.exe \
      "${WEBVIEW2_RUNTIME_URL}" \
    && xvfb-run -a wine /tmp/MicrosoftEdgeWebView2RuntimeInstallerX64.exe \
      /silent /install \
    && wineserver -k \
    && find "/home/wine/.wine/drive_c/Program Files (x86)/Microsoft/EdgeWebView/Application" \
      -mindepth 2 -maxdepth 2 -name msedgewebview2.exe -print -quit \
      | grep -q . \
    && rm /tmp/MicrosoftEdgeWebView2RuntimeInstallerX64.exe

USER root
COPY --chmod=755 docker/run-tracker-wine.sh /usr/local/bin/run-tracker-wine

# Kestrel needs an explicit certificate because Wine does not provide the
# ASP.NET Core development certificate that UseHttps() normally discovers.
# This certificate is only used by Tracker's loopback server.
#
# We COPY a fixed, committed, known-good PFX instead of generating one at
# build time. A freshly generated PFX (Linux OpenSSL) yields a private key
# that Wine's Schannel/GnuTLS provider cannot sign with, which surfaces as
# "GnuTLS error: Public key signing has failed" and breaks WebView2's
# navigation to https://localhost:7101. The committed PFX is a throwaway
# self-signed localhost dev cert (its password is already hardcoded in the
# app and this Dockerfile), so committing it is safe and makes builds
# deterministic.
RUN mkdir -p /opt/tracker
COPY --chmod=0444 docker/certs/localhost.pfx /opt/tracker/localhost.pfx
USER wine

# Pre-create the Tracker user-data directory so Docker seeds the
# tracker-user-data named volume with wine-owned ownership (matching
# how the Wine prefix at /home/wine/.wine is seeded into tracker-wine-data).
RUN mkdir -p "/home/wine/.local/share/Videre Tracker"

CMD ["run-tracker-wine"]
