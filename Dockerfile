
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PDFOrganizerAPI/*.csproj ./PDFOrganizerAPI/
RUN dotnet restore ./PDFOrganizerAPI/PDFOrganizerAPI.csproj

COPY PDFOrganizerAPI/ ./PDFOrganizerAPI/

WORKDIR /src/PDFOrganizerAPI
RUN dotnet publish PDFOrganizerAPI.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PDFOrganizerAPI.dll"]
