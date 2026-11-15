# Browser-only Linux desktop for inspecting IterVC's Avalonia UI.
# The GitHub Codespaces forwarded port (6080) remains PRIVATE by default.
FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    xvfb x11vnc x11-utils xauth openbox xterm \
    novnc websockify dbus-x11 dbus-user-session \
    libfontconfig1 libice6 libsm6 libgl1 libxkbcommon0 \
    fonts-dejavu-core ca-certificates curl \
    pipewire pipewire-bin pipewire-pulse wireplumber pulseaudio-utils \
    && rm -rf /var/lib/apt/lists/*
