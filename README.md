# EmpleadoDelMes — Backend

API desarrollada con ASP.NET Core y .NET 10 para el sistema
EmpleadoDelMes.

## Estado del proyecto

En desarrollo. Actualmente se está preparando la estructura
técnica. Las funcionalidades de negocio están pendientes
de análisis y diseño.

## Tecnologías

- C# / .NET 10
- ASP.NET Core Web API
- Arquitectura Onion
- MSTest para pruebas unitarias
- NuGet Central Package Management

## Estructura

- src/EmpleadoDelMes.Api: entrada HTTP y configuración.
- src/EmpleadoDelMes.Application: casos de uso.
- src/EmpleadoDelMes.Domain: modelo y reglas de negocio.
- src/EmpleadoDelMes.Infrastructure: implementaciones externas.
- tests/EmpleadoDelMes.UnitTests: pruebas automatizadas.

## Requisitos

- .NET 10 SDK
- Git
- Visual Studio 2026 o editor compatible

## Ejecutar localmente

Desde la raíz del repositorio:

```powershell
dotnet restore EmpleadoDelMes.slnx
dotnet build EmpleadoDelMes.slnx
dotnet run --project src/EmpleadoDelMes.Api --launch-profile https
```

La API se ejecuta en el puerto indicado por la terminal.

## Endpoints técnicos

Disponibles durante desarrollo:

- GET /api/ping
- GET /health
- GET /swagger (interfaz Swagger UI)

## Pruebas

```powershell
dotnet test EmpleadoDelMes.slnx
```

## Configuración

La configuración se organiza mediante appsettings.json
y archivos específicos por ambiente.

No almacenar contraseñas, tokens ni credenciales en Git.
Utilizar User Secrets en desarrollo y configuración segura
en Azure para producción.

## Despliegue

Pendiente de configuración.

Destino previsto:
- Backend: Azure App Service
- CI/CD: GitHub Actions

## Licencia

Proyecto interno. Licenciamiento y permisos de distribución
pendientes de definición por la organización.
