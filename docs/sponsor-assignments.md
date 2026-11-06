# Asignaciones de patrocinadores — Fase 1

Todas las rutas usan el prefijo `/api/admin/sponsor-assignments`, política Administrator y los controles actuales de sesión. No se modifican autenticación, postulaciones u opiniones.

| Método | Ruta relativa | Propósito |
|---|---|---|
| GET | /sponsors | Empleados con cuenta activa y rol Sponsor |
| GET | /sponsors/{sponsorId}/employees | Colaboradores con asignación vigente |
| GET | /employees/available | Empleados sin asignación; sponsorId opcional excluye autoasignación |
| GET | /employees/pending | Todos los empleados sin asignación vigente |
| GET | /employees/{employeeId}/current | Asignación vigente y versión |
| POST | /employees/{employeeId}/assign | Primera asignación, 201 y Location a current |
| PUT | /employees/{employeeId}/reassign | Retirar anterior y crear nueva, 200 |
| POST | /employees/{employeeId}/withdraw | Retirar sin borrar, 200 |

Listados: term opcional (hasta 100 caracteres), pageNumber=1, pageSize=20 (1–100); orden por nómina e ID, filtrado y paginación SQL. Respuesta Items/Total/PageNumber/PageSize con Id, PayrollNumber, FullName por empleado. Available y Pending significan ausencia de relación activa; al guardar se vuelve a comprobar validez del patrocinador y ciclos. No se excluyen empleados administrativos ni patrocinadores como colaboradores. Un patrocinador desactivado conserva su historial y sus colaboradores pueden consultarse y reasignarse.

Assign recibe `{"sponsorEmployeeId":"GUID"}`. Reassign recibe `{"sponsorEmployeeId":"GUID","version":"BASE64"}`. Withdraw recibe `{"version":"BASE64"}`. Se rechazan campos ajenos. Current responde `{"assignment":null}` con 200 cuando el empleado existe sin relación vigente; si existe devuelve Id, SponsorEmployeeId, EmployeeId, IsActive, CreatedAtUtc, UpdatedAtUtc y Version dentro de assignment. No se exponen cuentas ni hashes. Un empleado inexistente produce 404.

HTTP: 400 datos inválidos o patrocinador sin cuenta Sponsor activa; 401/403 sesión y permisos; 404 empleado o asignación vigente inexistente; 409 duplicado, ciclo, versión obsoleta o conflicto; 503 persistencia. Asignar al mismo patrocinador existente responde 409. Retirar nuevamente responde 404 al no haber relación vigente. Reasignar exige una relación activa y versión de 8 bytes: no sustituye silenciosamente una asignación creada mientras el cliente tenía datos antiguos.

## Integridad

La entidad conserva identidades y fecha de creación. Withdraw marca IsActive=false y UpdatedAtUtc con TimeProvider. Cada reasignación crea un ID nuevo. FK NoAction a Employees; CHECK de autoasignación y fechas; índice único filtrado `[IsActive] = 1` sobre EmployeeId; RowVersion y índices para consultas por patrocinador e historia del empleado.

Las escrituras usan una transacción Serializable y `sp_getapplock` exclusivo `EmpleadoDelMes.SponsorAssignments.v1`, propiedad Transaction. Ninguna de estas operaciones adquiere los bloqueos de convocatorias/cuentas. Se sigue la cadena de asignaciones activas del patrocinador con un conjunto de visitados: llegar al colaborador o repetir un nodo rechaza el ciclo. No se carga un organigrama completo. La comprobación queda protegida de otras asignaciones entre instancias; SQL directo ajeno al protocolo no garantiza ausencia de ciclos.

Reasignar guarda primero el retiro para liberar el índice filtrado y después la nueva relación, siempre dentro de la misma transacción. Un fallo revierte ambos pasos. Se comparan y usan las versiones originales en UPDATE. Duplicados, deadlocks y conflictos se traducen a 409 sin revelar mensajes SQL. Identity se consulta para validar rol y cuenta activa sin alterar sus mecanismos. Una retirada posterior de rol o desactivación no retira automáticamente asignaciones.

## Migración y aplicación posterior

Generada `20261106200950_AddSponsorEmployeeAssignments`, sin aplicar. No se modifican migraciones anteriores. EF encontró un snapshot de respaldo bajo obj; el snapshot generado se trasladó a Persistence/Migrations/ApplicationDbContextModelSnapshot.cs. Revisar que futuras herramientas no seleccionen copias de snapshots en obj.

Para preparar un script revisable posteriormente (no ejecutado en esta tarea):

```powershell
dotnet ef migrations script 20261105155610_AddCampaignAutomationConfiguration 20261106200950_AddSponsorEmployeeAssignments --project src/EmpleadoDelMes.Infrastructure --startup-project src/EmpleadoDelMes.Api --configuration Release --output SponsorAssignments.sql -- --schema-only
```

Tras revisión y autorización del destino, aplicar ese script con las herramientas SQL del entorno. Alternativamente, en un entorno autorizado con la conexión obtenida de configuración segura:

```powershell
dotnet ef database update 20261106200950_AddSponsorEmployeeAssignments --project src/EmpleadoDelMes.Infrastructure --startup-project src/EmpleadoDelMes.Api --configuration Release --connection "$env:ConnectionStrings__DefaultConnection" -- --schema-only
```

La fábrica schema-only usa una conexión ficticia para construir el modelo; database update necesita explícitamente el override seguro de conexión indicado. No ejecutar con una variable vacía, no versionar conexiones ni contraseñas. Estos comandos son instrucciones futuras: no se abrió ninguna conexión SQL en esta tarea.

## Próxima fase

El historial describe periodos de vigencia: CreatedAtUtc hasta UpdatedAtUtc para relaciones retiradas. No congela por sí mismo el evaluador de una convocatoria. Antes de opiniones del jefe inmediato se debe aprobar el instante de corte y crear una vinculación inmutable convocatoria/colaborador/asignación (FK al ID histórico más instantáneas necesarias), con unicidad por convocatoria y colaborador. No resolver evaluaciones históricas mediante la asignación activa actual. Esa vinculación, opiniones, auditoría de actores y UI de historial no se implementan aquí.

No se crearon ni ejecutaron pruebas automatizadas por instrucción del proyecto. La revisión estática y compilación no acreditan concurrencia física, traducción de consultas ni rollback en SQL Server; quedan pendientes de validación autorizada.
