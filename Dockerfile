# ---------------------------------------------------------------- build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Najpierw same pliki projektów — dzięki temu warstwa z `restore` cachuje się między buildami.
COPY NuGet.config ./
COPY M2Manager.sln ./
COPY M2Manager.Shared/M2Manager.Shared.csproj M2Manager.Shared/
COPY M2Manager.Client/M2Manager.Client.csproj M2Manager.Client/
COPY M2Manager.Api/M2Manager.Api.csproj M2Manager.Api/

RUN dotnet restore M2Manager.Api/M2Manager.Api.csproj

COPY M2Manager.Shared/ M2Manager.Shared/
COPY M2Manager.Client/ M2Manager.Client/
COPY M2Manager.Api/ M2Manager.Api/

# API publikuje też statyczne pliki Blazora (model ASP.NET Core Hosted).
RUN dotnet publish M2Manager.Api/M2Manager.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------------- runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# QuestPDF renderuje PDF-y przez SkiaSharp, który na Linuksie potrzebuje fontconfig.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

# Obraz Debiana ma ICU, więc polskie formatowanie liczb i dat działa.
# Nie ustawiaj DOTNET_SYSTEM_GLOBALIZATION_INVARIANT — zepsułoby to nazwy miesięcy i separatory.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "M2Manager.Api.dll"]
