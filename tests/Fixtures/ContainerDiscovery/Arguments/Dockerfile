ARG REGISTRY=mcr.microsoft.com
ARG FAMILY=aspnet
ARG TAG=10.0
FROM ${REGISTRY}/dotnet/${FAMILY}:$TAG AS app
ARG LATE=mcr.microsoft.com/dotnet/runtime:10.0
FROM $UNKNOWN AS unknown
FROM ${LATE}
FROM registry.example/dotnet/sdk:10.0
