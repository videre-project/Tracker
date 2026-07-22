FROM videreproject/mtgosdk:wayland AS tracker-prereqs

ARG WEBVIEW2_RUNTIME_URL=https://go.microsoft.com/fwlink/?linkid=2124701

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
RUN mkdir -p /opt/tracker \
    && openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 3650 \
      -subj "/CN=localhost" \
      -addext "subjectAltName=DNS:localhost,IP:127.0.0.1,IP:::1" \
      -keyout /tmp/tracker-localhost.key \
      -out /tmp/tracker-localhost.crt \
    && openssl pkcs12 -export \
      -out /opt/tracker/localhost.pfx \
      -inkey /tmp/tracker-localhost.key \
      -in /tmp/tracker-localhost.crt \
      -passout pass:tracker-localhost \
    && chmod 0444 /opt/tracker/localhost.pfx \
    && rm /tmp/tracker-localhost.key /tmp/tracker-localhost.crt
USER wine

CMD ["run-tracker-wine"]
