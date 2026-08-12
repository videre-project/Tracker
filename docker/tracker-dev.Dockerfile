# Development Dockerfile for Tracker with Vite HMR and .NET SpaProxy
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-sdk

FROM videreproject/mtgosdk:wayland AS tracker-dev-base

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
      curl \
      gnupg \
      socat \
    && rm -rf /var/lib/apt/lists/*

# Install Node.js 22
RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get update \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install pnpm
RUN npm install -g pnpm

USER wine

# Tracker's native WebView2 loader uses the current Visual C++ runtime family.
RUN xvfb-run -a winetricks -q --force vcrun2022

# The current runtime rejects Wine's Windows 7 default, so configure this
# Tracker-specific prefix as Windows 10 before running Microsoft's installer.
RUN xvfb-run -a winetricks -q win10

# Install .NET Desktop Runtime 10.0 for Windows Forms support
RUN xvfb-run -a winetricks -q dotnetdesktop10

USER root
COPY --from=dotnet-sdk /usr/share/dotnet /opt/dotnet
RUN ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet
USER wine

# Compose mounts a persistent Wine prefix over the image's prefix. Preserve a
# stable runtime outside it so old volumes cannot downgrade build or startup.
ARG DOTNET_RUNTIME_VERSION=10.0.0
RUN cp -a '/home/wine/.wine/drive_c/Program Files/dotnet' /home/wine/windows-dotnet

RUN curl --fail --location --retry 3 \
      --output /tmp/aspnetcore-runtime.zip \
      "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/${DOTNET_RUNTIME_VERSION}/aspnetcore-runtime-${DOTNET_RUNTIME_VERSION}-win-x64.zip" \
    && unzip -qo /tmp/aspnetcore-runtime.zip -d /home/wine/windows-dotnet \
    && rm /tmp/aspnetcore-runtime.zip

# Keep the NSwag shim outside the mutable Wine prefix mounted by Compose.
RUN wine /home/wine/.wine/drive_c/dotnet/dotnet.exe tool install \
      nswag.consolecore \
      --tool-path 'Z:\home\wine\tracker-tools' \
    && wine /home/wine/.wine/drive_c/dotnet/dotnet.exe tool install \
      swashbuckle.aspnetcore.cli \
      --version 10.1.0 \
      --tool-path 'Z:\home\wine\tracker-tools'

FROM tracker-dev-base AS tracker-dev

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
COPY --chmod=755 docker/run-tracker-dev.sh /usr/local/bin/run-tracker-dev

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

# Create workspace directory and set permissions
RUN mkdir -p /workspace/src/client /workspace/src/server \
    && chown -R wine:wine /workspace

USER wine
WORKDIR /workspace

CMD ["run-tracker-dev"]
