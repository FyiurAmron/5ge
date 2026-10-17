# Reference build image: system, .NET SDK, Godot mono editor and export
# templates (Windows, Linux and the rest ship in one tpz).
#
# The toolchain versions are build args with no defaults on purpose:
# install_deps.sh is the single source of truth, CI passes them in, and
# _tools/image-tag.sh derives the image tag from the same values. This file
# copies that script and asks it for the URLs and paths rather than repeating
# them, so a version bump is a one-line edit in one place. The editor lands in
# /usr/local/bin, on the PATH of every process, and root owns that directory
# here, so no sudo is involved.
#
# The COPY sits after the .NET layer on purpose: it invalidates only the
# layers below it, which keeps the SDK download cached.
#
# The RUN steps use `set -eu`, not `set -euxo pipefail`: Docker runs them with
# /bin/sh (dash on this base), which has no pipefail, and none of them pipe.
# Pure ASCII.
FROM ubuntu:24.04

ARG DOTNET_CHANNEL
ARG DOTNET_QUALITY
ARG GODOT_TAG

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_ROOT=/root/.dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl unzip python3 jq \
    libfontconfig1 libfreetype6 libicu74 libgl1 libx11-6 libxcursor1 \
    libxrandr2 libxinerama1 libxi6 libxext6 libasound2t64 libpulse0 \
    libdbus-1-3 libudev1 \
 && rm -rf /var/lib/apt/lists/*

# .NET SDK, same channel as install_deps.sh.
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && chmod +x /tmp/dotnet-install.sh \
 && /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --quality "$DOTNET_QUALITY" --install-dir "$DOTNET_ROOT" \
 && rm /tmp/dotnet-install.sh
ENV PATH="/root/.dotnet:/root/.dotnet/tools:${PATH}"

COPY _tools/install_deps.sh /usr/local/share/install_deps.sh

# Godot mono editor, installed as `godot` where every process finds it.
# GodotSharp is the .NET assembly set and has to sit beside the executable:
# a lone binary aborts with ".NET: Assemblies not found".
RUN set -eu \
 && eval "$(GODOT_LINK_DIR=/usr/local/bin bash /usr/local/share/install_deps.sh --print-versions)" \
 && mkdir -p "$GODOT_LINK_DIR" \
 && curl -sSL -o /tmp/godot.zip "$GODOT_URL" \
 && unzip -o -q /tmp/godot.zip -d /tmp/editor \
 && mv /tmp/editor/Godot_v${GODOT_TAG}_mono_linux_x86_64/Godot_v${GODOT_TAG}_mono_linux.x86_64 "$GODOT_BIN" \
 && mv /tmp/editor/Godot_v${GODOT_TAG}_mono_linux_x86_64/GodotSharp "$GODOT_LINK_DIR/" \
 && rm -rf /tmp/editor /tmp/godot.zip \
 && chmod 0755 "$GODOT_BIN" \
 && "$GODOT_BIN" --version \
 && ls "$GODOT_LINK_DIR"

# Export templates, into the directory Godot looks in. Godot names that
# directory after the version with dashes turned into dots
# (4.8-dev7 -> 4.8.dev7.mono).
RUN set -eu \
 && eval "$(bash /usr/local/share/install_deps.sh --print-versions)" \
 && curl -sSL -o /tmp/templates.tpz "$GODOT_TEMPLATES_URL" \
 && unzip -o -q /tmp/templates.tpz -d /tmp/tpz \
 && mkdir -p "$GODOT_TEMPLATE_DIR" \
 && mv /tmp/tpz/templates/* "$GODOT_TEMPLATE_DIR"/ \
 && rmdir /tmp/tpz/templates \
 && rm -rf /tmp/tpz /tmp/templates.tpz \
 && ls "$GODOT_TEMPLATE_DIR" | head -3

# Set the runtime install location after the expensive template layer so
# changing this path does not invalidate the editor/template download cache.
ENV GODOT_LINK_DIR=/usr/local/bin
