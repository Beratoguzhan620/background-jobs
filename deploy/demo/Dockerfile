# The public demo in one container: the API and one worker side by side, so a single free web service (Render)
# runs the whole thing next to a hosted PostgreSQL (Neon). Compose runs them as separate containers instead.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props BackgroundJobs.slnx ./
COPY src/ src/
RUN dotnet publish src/BackgroundJobs.Api/BackgroundJobs.Api.csproj -c Release -o /app/api /p:UseAppHost=false \
 && dotnet publish src/BackgroundJobs.Worker/BackgroundJobs.Worker.csproj -c Release -o /app/worker /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/api ./api
COPY --from=build /app/worker ./worker
COPY deploy/demo/start.sh ./start.sh
USER $APP_UID
ENTRYPOINT ["sh", "/app/start.sh"]
