# syntax=docker/dockerfile:1

# Microsoft publishes both required SDK families on Ubuntu 24.04 Noble for
# linux/amd64 and linux/arm64. Version tags are resolved in CI and are pinned to
# their immutable manifest digests after the multi-architecture/security check.
FROM mcr.microsoft.com/dotnet/sdk:8.0.424-noble AS dotnet8

# This stage follows TARGETPLATFORM and provides the architecture-correct .NET 10
# muxer/runtime/SDK files copied into the final multi-architecture image.
FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble AS dotnet10

# The application itself is framework-dependent/architecture-neutral, so compile
# on BUILDPLATFORM to avoid emulating the SDK during the publish stage.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.400-noble AS build

WORKDIR /src
COPY . .

RUN dotnet restore ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj \
    && dotnet publish ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj \
        --configuration Release \
        --no-restore \
        --output /out \
        /p:UseAppHost=false

# Use the minimal Microsoft runtime-deps image for the operating-system layer,
# then copy only the .NET installation required for SDK selection and MSBuild
# inspection. Noble is the default Linux distribution for .NET 10 and avoids the
# Azure Linux Expat fixes that are announced upstream but not yet published in
# the Azure Linux package feed used by the pinned 10.0.11 image.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.11-noble AS final

COPY --from=dotnet10 /usr/share/dotnet/ /usr/share/dotnet/

# Preserve the repository's supported SDK matrix inside one image. The .NET 10
# SDK remains authoritative for the dotnet muxer; the versioned .NET 8 host,
# SDK, runtime, targeting packs, and workload manifests are overlaid side-by-side.
# Workloads themselves are outside the supported container contract, so the
# documented first-run workload integrity check is skipped below while normal
# MSBuild workload resolution semantics remain unchanged.
COPY --from=dotnet8 /usr/share/dotnet/host/ /usr/share/dotnet/host/
COPY --from=dotnet8 /usr/share/dotnet/packs/ /usr/share/dotnet/packs/
COPY --from=dotnet8 /usr/share/dotnet/sdk/ /usr/share/dotnet/sdk/
COPY --from=dotnet8 /usr/share/dotnet/sdk-manifests/ /usr/share/dotnet/sdk-manifests/
COPY --from=dotnet8 /usr/share/dotnet/shared/ /usr/share/dotnet/shared/

COPY --from=build /out/ /opt/dotnet-repo-inspector/

ENV DOTNET_CLI_HOME=/tmp/dotnet-home \
    DOTNET_NOLOGO=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true \
    DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=true \
    NUGET_PACKAGES=/tmp/nuget/packages \
    NUGET_XMLDOC_MODE=skip \
    HOME=/tmp \
    XDG_CACHE_HOME=/tmp/.cache

# runtime-deps intentionally has no dotnet executable. Expose the copied muxer,
# remove optional SDK tooling that is not part of the Inspector contract, and
# prepare the documented source/output mount points for the non-root app user.
RUN ln --symbolic /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm -rf /usr/share/dotnet/sdk/8.0.424/DotnetTools/dotnet-format \
    && mkdir --parents /repo /artifacts \
    && chown "$APP_UID:$APP_UID" /repo /artifacts

LABEL org.opencontainers.image.title="DotNetRepoInspector" \
      org.opencontainers.image.description="Deterministic inspection and classification of .NET repositories using evaluated MSBuild metadata." \
      org.opencontainers.image.source="https://github.com/rodri-oliveira-dev/DotNetRepoInspector" \
      org.opencontainers.image.documentation="https://github.com/rodri-oliveira-dev/DotNetRepoInspector/blob/main/docs/en/container.md" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.authors="Rodrigo de Oliveira"

WORKDIR /repo
USER $APP_UID

ENTRYPOINT ["dotnet", "/opt/dotnet-repo-inspector/DotNetRepoInspector.Cli.dll"]
