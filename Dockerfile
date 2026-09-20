FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
USER root
RUN apk add --no-cache icu-libs krb5-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM node:22-alpine AS client-build
WORKDIR /src/ClientApp
COPY ["ClientApp/package.json", "ClientApp/package-lock.json", "./"]
RUN npm ci
COPY ClientApp/ .
ARG VITE_TELEGRAM_BOT_URL
ENV VITE_TELEGRAM_BOT_URL=$VITE_TELEGRAM_BOT_URL
RUN npm run build

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
COPY --from=client-build /src/ClientApp/dist ./wwwroot
ENTRYPOINT ["dotnet", "SplitMoneyTg.dll"]
