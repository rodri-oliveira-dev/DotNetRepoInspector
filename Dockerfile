# syntax=docker/dockerfile:1

# Microsoft publishes both required SDK families on Ubuntu 24.04 Noble for
# linux/amd64 and linux/arm64. Keep human-readable version tags and pin each
# multi-platform manifest by immutable digest.
FROM mcr.microsoft.com/dotnet/sdk:8.0.424-noble@sha256:2ae6f287fa860c15f121474cf864b86765beb87507bbc3f48661a4f6f1ffc2b5 AS dotnet8

# This stage follows TARGETPLATFORM and provides the architecture-correct .NET 10
# muxer/runtime/SDK files copied into the final multi-architecture image.
FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS dotnet10

# The application itself is framework-dependent/architecture-neutral, so compile
# on BUILDPLATFORM to avoid emulating the SDK during the publish stage.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build

ARG PRODUCT_VERSION=0.0.0
ARG REPOSITORY_COMMIT=local

WORKDIR /src
COPY . .

RUN dotnet restore ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj \
    && dotnet publish ./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj \
        --configuration Release \
        --no-restore \
        --output /out \
        /p:UseAppHost=false \
        /p:Version="${PRODUCT_VERSION}" \
        /p:RepositoryCommit="${REPOSITORY_COMMIT}"

# Use the minimal Microsoft runtime-deps image for the operating-system layer,
# then copy only the .NET installation required for SDK selection and MSBuild
# inspection. Noble avoids the Azure Linux package findings seen in the previous
# composition while preserving Microsoft's supported container baseline.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.11-noble@sha256:9b37bbaf06fc653cb0e757215081139fb493658e1f864a738f6a478620c9196f AS final

COPY --from=dotnet10 /usr/share/dotnet/ /usr/share/dotnet/

# Preserve the repository's supported SDK matrix inside one image. The .NET 10
# SDK remains authoritative for the dotnet muxer; the serviced .NET 8 SDK is
# overlaid side-by-side and satisfies the repository's 8.0.100 + latestFeature
# fixture. Workloads themselves are outside the supported container contract, so
# only the documented first-run workload integrity check is skipped.
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
# remove optional .NET 8 SDK tooling that is not part of the Inspector contract,
# and prepare the documented source/output mount points for the non-root app user.
RUN ln --symbolic /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm -rf /usr/share/dotnet/sdk/8.0.*/DotnetTools/dotnet-format \
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