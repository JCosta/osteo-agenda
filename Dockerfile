FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Osteo.Agenda.Core/Osteo.Agenda.Core.csproj src/Osteo.Agenda.Core/
COPY src/Osteo.Agenda.Web/Osteo.Agenda.Web.csproj src/Osteo.Agenda.Web/
RUN dotnet restore src/Osteo.Agenda.Web/Osteo.Agenda.Web.csproj
COPY src/ src/
RUN dotnet publish src/Osteo.Agenda.Web/Osteo.Agenda.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:10000 \
    TZ=Europe/Lisbon
EXPOSE 10000
USER $APP_UID
ENTRYPOINT ["dotnet", "Osteo.Agenda.Web.dll"]
