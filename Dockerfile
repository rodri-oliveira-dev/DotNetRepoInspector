# syntax=docker/dockerfile:1

# Keep each Microsoft image reference readable and immutable. The multi-platform
# manifest digests are intentionally pinned; Dependabot servicing is handled by #103.
FROM mcr.microsoft.com/dotnet/sdk:8.0.424-azurelinux3.0@sha256:6e8e68891aeff6ce36b558e27a897a062d5bf425d5f400a2a1fbdfa5bbd0921c AS dotnet8

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.400-azurelinux3.0@sha256:148df6ae5a1a242c4d737aecea047eabd7764c05f9d7016433ce64d6bb6fe00c AS build

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
# inspection. This avoids carrying unrelated SDK-image OS tooling into runtime.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.11-azurelinux3.0@sha256:b9695c27ae6a28fcb49740f8e0d94fb361ab2a03eb702e9e43b89d5dfdb52e0b AS final

COPY --from=build /usr/share/dotnet/ /usr/share/dotnet/

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
