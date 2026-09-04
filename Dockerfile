# syntax=docker/dockerfile:1

# Keep each Microsoft SDK reference readable and immutable. The multi-platform
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

FROM mcr.microsoft.com/dotnet/sdk:10.0.400-azurelinux3.0@sha256:148df6ae5a1a242c4d737aecea047eabd7764c05f9d7016433ce64d6bb6fe00c AS final

# Preserve the repository's supported SDK matrix inside one image. The .NET 10
# SDK image stays authoritative for the dotnet muxer; the versioned .NET 8 host,
# SDK, runtime, targeting packs, workload manifests, and SDK workload metadata
# are overlaid side-by-side. Workloads themselves are outside the supported
# container contract and the workload resolver is disabled below so it cannot
# contaminate structured MSBuild output while validating an unsupported surface.
COPY --from=dotnet8 /usr/share/dotnet/host/ /usr/share/dotnet/host/
COPY --from=dotnet8 /usr/share/dotnet/packs/ /usr/share/dotnet/packs/
COPY --from=dotnet8 /usr/share/dotnet/sdk/ /usr/share/dotnet/sdk/
COPY --from=dotnet8 /usr/share/dotnet/sdk-manifests/ /usr/share/dotnet/sdk-manifests/
COPY --from=dotnet8 /usr/share/dotnet/shared/ /usr/share/dotnet/shared/
COPY --from=dotnet8 /usr/share/dotnet/metadata/ /usr/share/dotnet/metadata/

COPY --from=build /out/ /opt/dotnet-repo-inspector/

ENV DOTNET_CLI_HOME=/tmp/dotnet-home \
    DOTNET_NOLOGO=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true \
    MSBuildEnableWorkloadResolver=false \
    NUGET_PACKAGES=/tmp/nuget/packages \
    NUGET_XMLDOC_MODE=skip \
    HOME=/tmp \
    XDG_CACHE_HOME=/tmp/.cache

RUN mkdir --parents /repo /artifacts \
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
