# Stage base
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Stage build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Baixar o script wait-for-it.sh
RUN curl -o /wait-for-it.sh https://raw.githubusercontent.com/vishnubob/wait-for-it/master/wait-for-it.sh \
    && chmod +x /wait-for-it.sh

# Copia apenas os csproj para cache eficiente
COPY FluxoCaixaApi/FluxoCaixaApi.csproj FluxoCaixaApi/
COPY CommandStore/CommandStore.csproj CommandStore/
COPY FluxoCaixa/FluxoCaixa.csproj FluxoCaixa/
COPY Command/Command.csproj Command/
COPY Contract/Contract.csproj Contract/
COPY Enumeration/Enumeration.csproj Enumeration/

RUN dotnet restore FluxoCaixaApi/FluxoCaixaApi.csproj

# Copia todo o código
COPY . .

WORKDIR /src/FluxoCaixaApi
RUN dotnet build FluxoCaixaApi.csproj -c $BUILD_CONFIGURATION -o /app/build

# Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish FluxoCaixaApi.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FluxoCaixaApi.dll"]
