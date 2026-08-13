FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
USER root
RUN apk add --no-cache icu-libs krb5-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SplitMoneyTg.csproj", "./"]
RUN dotnet restore "SplitMoneyTg.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "./SplitMoneyTg.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SplitMoneyTg.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SplitMoneyTg.dll"]
