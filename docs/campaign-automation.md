# Automatización mensual configurable desde Administración

Actualización anticipada: ver [automatizacion-anticipada.md](automatizacion-anticipada.md). Los resultados de tests al final pertenecen a la entrega inicial; no se ejecutaron tests para este cambio.

## Alcance y arquitectura

Implementación en la rama de trabajo `cambios` del backend. No se modificó el frontend, Azure, autenticación, roles ni bases reales. No se inició la API local contra SQL Server; el host HTTP usado por las pruebas sustituye los repositorios y Identity por dobles en memoria y desactiva bootstrap y worker.

- **Dominio:** `CampaignAutomationConfiguration`, singleton con Id=1, calendario opcional inicialmente, activación, responsable, fecha UTC y RowVersion. `CampaignAutomationCalendar` contiene los cuatro desplazamientos/horas. `CampaignAutomationAuditEntry` registra valores anteriores/nuevos no sensibles.
- **Aplicación:** contratos, validación, vista previa y coordinación mediante `CampaignAutomationService`. Reutiliza `CampaignSchedule`, `CampaignService` y `CampaignNominationPolicy`.
- **Infraestructura:** EF almacena configuración y auditoría. `CampaignAutomationStore` comparte `CampaignAdministrationLock` con altas y operaciones administrativas existentes. El bloqueo es transaccional y exclusivo mediante SQL Server, no un mutex local.
- **API:** endpoints administrativos y ciclo del worker. Se mantienen TimeProvider, PeriodicTimer, cancelación y reintento en el siguiente intervalo. El calendario administrativo se vuelve a leer en SQL bajo bloqueo antes de cada posible creación; no existe la antigua caché `completedLocalMonth`.

La generación efectiva requiere permiso técnico, activación SQL y ausencia de configuración heredada. Cada mes usa una transacción: releer configuración, comprobar existencia, construir ambos periodos Automatic, validar exclusividad y confirmar. CampaignRepository participa en esa transacción sin abrir ni confirmar una transacción anidada. La creación manual conserva su transacción propia. El índice único EvaluationMonth sigue siendo la última defensa contra duplicados.

La pausa confirmada queda serializada respecto de la inserción: una generación que obtuvo primero el bloqueo puede terminar antes de la pausa; una generación posterior al commit de la pausa no inserta. Los meses ya confirmados permanecen si un mes posterior falla. Ningún proceso modifica convocatorias existentes, incluidas archivadas, manuales o con horarios excepcionales/evaluaciones.

## Configuración técnica y calendario SQL

Infraestructura conserva exclusivamente:

| Ajuste | Uso |
|---|---|
| CampaignAutomation__Enabled | Interruptor técnico principal; ausente/false no inicia consultas del worker |
| CampaignAutomation__TimeZoneId | America/Monterrey; otro valor se rechaza si el interruptor está activo |
| CampaignAutomation__CheckIntervalMinutes | Entero de 1 a 1440; predeterminado 60 |
| CampaignAutomation__FirstEvaluationMonth | Primer mes recuperable, yyyy-MM-01; obligatorio al activar, año máximo 9997 |

No se modificaron los valores actuales. Estos ajustes técnicos se toman durante el ciclo de vida del proceso; sus cambios requieren reinicio. Los cambios de negocio guardados en SQL se aplican en las comprobaciones siguientes sin reinicio.

Los desplazamientos son días desde el **primer día del mes posterior al mes evaluado**: para octubre de 2026, offset 0 representa 1 de noviembre. Cada hora local debe tener formato HH:mm; offset admitido -31..365. Se convierte mediante America/Monterrey, nunca mediante un offset UTC fijo. Se rechazan apertura >= cierre, horas ambiguas/inexistentes y solapamientos recurrentes de postulaciones. La validación recorre los meses soportados desde el límite técnico hasta 9997, incluyendo cambios de calendario.

La configuración inicial de la migración tiene IsEnabled=false y Calendar=null. Una actualización requiere calendario completo incluso si se guarda deshabilitada. Al activar se valida también contra convocatorias existentes bajo el bloqueo administrativo; un conflicto impide guardar y devuelve 409. Al pausar no se exige resolver conflictos operativos con aperturas excepcionales existentes. Guardar deshabilitado nunca altera los periodos de convocatorias ya registradas.

El límite técnico determina la recuperación de meses faltantes. Se puede crear el mes local actual desde su primer día; nunca se generan meses posteriores al actual. FirstEvaluationMonth continúa siendo el límite inferior de recuperación. Nuevos calendarios se aplican a los meses autorizados que todavía no existen, también al recuperar pendientes; conviene revisar esos pendientes antes de activar.

## Endpoints y ejemplos

Todos requieren Bearer válido, rol Administrator y contraseña definitiva. Se reutilizan las políticas y CurrentSessionMiddleware existentes. Otros roles reciben 403; sesión ausente/inválida 401; contraseña temporal 403. No hay endpoint para disparar generación ni para editar Azure.

### GET /api/admin/campaign-automation

Ejemplo ilustrativo de estado inicial con permiso técnico deshabilitado. La versión es opaca: usar siempre el valor recibido, no el ejemplo.

```json
{
  "configuration": {
    "isEnabled": false,
    "calendar": null,
    "version": "AAAAAAAAAAE=",
    "updatedAtUtc": null,
    "updatedByUserId": null
  },
  "technicalEnabled": false,
  "effectiveEnabled": false,
  "legacyScheduleDetected": false,
  "timeZone": "America/Monterrey",
  "firstEvaluationMonth": null,
  "operationalStatus": {
    "lastCheckAtUtc": null,
    "lastGenerationAtUtc": null,
    "lastGeneratedMonth": null,
    "error": null
  },
  "statusScope": "CurrentInstance"
}
```

El estado operativo es **por proceso**, se reinicia al reiniciar la instancia y no constituye un historial consolidado de Azure. LastGenerationAtUtc solo cambia tras commit de una creación real; encontrar una convocatoria existente no cuenta como generación. La auditoría de configuración sí es persistente. Los errores operativos son mensajes controlados, sin excepciones SQL ni credenciales.

### PUT /api/admin/campaign-automation

Ejemplo de borrador deshabilitado; enviar `isEnabled: true` únicamente después de aprobar la vista previa y el destino. El campo isEnabled es obligatorio.

```json
{
  "isEnabled": false,
  "calendar": {
    "nominationOpeningDayOffset": -8,
    "nominationOpeningLocalTime": "00:00",
    "nominationClosingDayOffset": 7,
    "nominationClosingLocalTime": "00:00",
    "opinionOpeningDayOffset": -8,
    "opinionOpeningLocalTime": "00:00",
    "opinionClosingDayOffset": 7,
    "opinionClosingLocalTime": "00:00"
  },
  "version": "VERSION_OBTENIDA_DEL_GET"
}
```

Devuelve 200 con el mismo contrato de GET y la nueva versión. El servidor obtiene UpdatedByUserId de la identidad validada, no del cuerpo. La transacción guarda configuración y auditoría juntas. RowVersion se compara después de adquirir el bloqueo y también se usa como token de concurrencia EF.

Errores:
- 400: calendario incompleto/inválido, versión mal formada o intento de activar mientras quedan claves heredadas.
- 409: versión obsoleta, bloqueo/concurrencia o conflicto con convocatorias.
- 503: persistencia no disponible o migración inicial ausente.

Ejemplo de respuesta 409:

```json
{ "message": "La configuración cambió. Vuelve a consultarla antes de guardar." }
```

### POST /api/admin/campaign-automation/preview

No guarda configuración, auditoría ni convocatorias. Recibe:

```json
{
  "calendar": {
    "nominationOpeningDayOffset": -8,
    "nominationOpeningLocalTime": "00:00",
    "nominationClosingDayOffset": 7,
    "nominationClosingLocalTime": "00:00",
    "opinionOpeningDayOffset": -8,
    "opinionOpeningLocalTime": "00:00",
    "opinionClosingDayOffset": 7,
    "opinionClosingLocalTime": "00:00"
  },
  "upcomingMonths": 3
}
```

UpcomingMonths admite 1..24 y agrega meses evaluados desde el mes local actual; también se incluyen los meses del rango de recuperación. Ejemplo de una entrada pendiente, para una consulta realizada en noviembre de 2026:

```json
{
  "timeZone": "America/Monterrey",
  "months": [
    {
      "evaluationMonth": "2026-10-01",
      "dates": {
        "nominationOpening": "2026-10-24T06:00:00+00:00",
        "nominationClosing": "2026-11-08T06:00:00+00:00",
        "opinionOpening": "2026-10-24T06:00:00+00:00",
        "opinionClosing": "2026-11-08T06:00:00+00:00"
      },
      "alreadyExists": false,
      "existingCampaignId": null,
      "isArchived": false,
      "isPending": true,
      "conflicts": [],
      "existingCampaign": null
    }
  ],
  "pendingMonths": ["2026-10-01", "2026-11-01"]
}
```

El ejemplo muestra solo la entrada de octubre por brevedad; omite la entrada pendiente de noviembre y las futuras. Los pendientes incluyen el mes actual si falta. `dates` son las fechas propuestas; cuando alreadyExists=true, `existingCampaign` muestra los periodos realmente guardados, que **no se modificarán**. Esa vista informativa no entrega una versión para editar la convocatoria: consultarla en su endpoint administrativo antes de modificarla. La vista previa no reserva fechas; el guardado y cada generación revalidan conflictos.

## Migración y transición segura

Migración generada: `20261105155610_AddCampaignAutomationConfiguration`.

Crea solamente CampaignAutomationConfiguration y CampaignAutomationAudit, sus claves/índice y la fila inicial deshabilitada. No actualiza Campaigns ni otras tablas de negocio. El snapshot quedó actualizado. No se aplicó ninguna migración.

La fábrica `SchemaOnlyContextFactory` exige `--schema-only`, no carga secretos ni inicia Program/host. Usa un destino local deliberadamente no operativo para las herramientas de generación offline. No es la conexión de la aplicación. Los comandos siguientes generan/inspeccionan archivos, **no aplican SQL**:

```powershell
dotnet build -c Release
dotnet ef migrations script 20261030164604_AddAccountIsActive 20261105155610_AddCampaignAutomationConfiguration --project src/EmpleadoDelMes.Infrastructure --startup-project src/EmpleadoDelMes.Infrastructure --configuration Release --no-build --output obj/campaign-automation-migration.sql -- --schema-only
```

Procedimiento pendiente, a realizar por responsables autorizados:

1. Confirmar servidor y base de desarrollo independientes sin revelar credenciales. Mantener el interruptor técnico false. No usar sa para la aplicación.
2. Revisar el script, respaldo y estrategia de despliegue. Aplicar la migración primero en desarrollo mediante el procedimiento aprobado de la organización. No se autoriza implícitamente su aplicación a producción.
3. **Antes de cualquier activación**, detener/deshabilitar el worker en todas las instancias y slots antiguos. Una instancia con el binario anterior no conoce el permiso SQL; no deben coexistir generaciones antiguas y nuevas.
4. Conservar un registro no sensible de los horarios heredados para revisión. Retirar **todas** las claves CampaignAutomation__Schedule__* de las fuentes aplicables: Azure, archivos, overrides, User Secrets y variables locales. Esto incluye Schedule__Enabled=false: su sola presencia bloquea la nueva generación. Revisar la precedencia de fuentes; eliminar un override no elimina una clave presente en appsettings.
5. Desplegar la versión nueva en todas las instancias manteniendo el interruptor técnico false. Confirmar GET con IsEnabled=false y LegacyScheduleDetected=false. Mantener el calendario técnico único America/Monterrey y revisar FirstEvaluationMonth.
6. Guardar un calendario SQL deshabilitado, revisar preview, pendientes, convocatorias históricas y aperturas manuales. No se importan ni activan automáticamente valores heredados.
7. Tras autorización, habilitar el permiso técnico y reiniciar las instancias nuevas; la activación SQL seguirá false. Posteriormente activar mediante PUT con versión vigente. Verificar que EffectiveEnabled=true y observar el siguiente ciclo.
8. Para pausar operativamente, PUT IsEnabled=false con calendario/versión vigentes. No cierra periodos ya persistidos. Para parada técnica, deshabilitar Enabled y reiniciar. Para rollback de binarios, **deshabilitar primero el permiso técnico en todas las instancias**: el código anterior ignora la pausa SQL.

La presencia de cualquier sección Schedule heredada bloquea el worker sin leer ese calendario, publica un error operativo controlado y prohíbe activación administrativa. No existe fallback al calendario de variables. El frontend futuro solo utiliza estos endpoints; no necesita acceso a Azure.

## Archivos creados o modificados

- API: `Automation/CampaignAutomationSettings.cs`, `Automation/CampaignAutomationCycle.cs`, `Controllers/CampaignAutomationController.cs`, `Program.cs`.
- Application: `CampaignAutomation/AutomationContracts.cs`, `AutomationCalendarValidation.cs`, `CampaignAutomationService.cs`; extracción de constructor compartido en `Campaigns/CampaignService.cs`.
- Domain: `Entities/CampaignAutomationConfiguration.cs` (configuración, calendario y auditoría).
- Infrastructure: `CampaignAutomation/CampaignAutomationStore.cs`, `DependencyInjection.cs`, `Persistence/Configurations/CampaignAutomationConfigurationMapping.cs`, `Persistence/CampaignRepository.cs`, `Persistence/SchemaOnlyContextFactory.cs`.
- EF: migración indicada, su Designer y `ApplicationDbContextModelSnapshot.cs`.
- Pruebas: `Campaigns/AutomationTestStore.cs`, `Campaigns/PersistentAutomationTests.cs`, `Campaigns/CampaignAutomationTests.cs`, `Authentication/AuthHttpTests.cs`, `Persistence/ModelTests.cs`.
- Documentación: este archivo sustituye la guía anterior de calendario por variables.

## Verificación histórica de la entrega inicial y límites

- Build Release: correcto, 0 errores y 0 advertencias.
- Última ejecución completa de la suite, anterior a la instrucción de detener los tests: **335 pruebas aprobadas, 0 fallos, 0 omitidas**. Después se ajustó la serialización de auditoría para excluir la versión anterior del registro de valores nuevos; ese ajuste compiló en Release, pero no se repitieron los tests por indicación del usuario. Incluye calendario inicial apagado, activación/pausa, permiso técnico, legado bloqueado, versiones, auditoría, conflictos, vista previa sin escrituras, cambios en caliente, duplicados, preservación de archivos/cambios manuales, temporizador/cancelación y recuperación de fallos simulados.
- HTTP en host de pruebas: GET/PUT/preview permitidos a Administrator definitivo; 401 sin sesión; 403 para los otros cuatro roles y contraseña temporal; 400 de validación y 409 de versión.
- Modelo EF: singleton, seed deshabilitado, RowVersion, auditoría y ausencia de diferencias pendientes entre modelo/snapshot.
- Script SQL generado e inspeccionado offline: únicamente nuevas tablas, seed deshabilitado e historial de migración.
- `git diff --check`: sin errores.

**No se ejecutaron pruebas contra SQL Server real.** Las pruebas de concurrencia usan un bloqueo simulado compartido; los fallos de almacenamiento se simulan. Falta verificar físicamente sp_getapplock entre conexiones/instancias, permisos SQL, rowversion, actualización del calendario owned, atomicidad de auditoría, rollback, deadlocks y recuperación tras caída. No se afirma validación completa de producción.

También pendientes: revisar la migración en una base de desarrollo autorizada, ensayar convivencia/retirada de instancias antiguas, comprobar ICU/tzdata de Linux, ajustar disponibilidad continua del host y revisar operativamente el backlog. El worker no garantiza ejecución exacta a medianoche: depende del intervalo, disponibilidad y reintentos. El estado abierto/cerrado de periodos existentes sigue evaluándose mediante las reglas de fecha/hora del dominio.

La telemetría de última comprobación/generación es local a la instancia y no durable; para historial operativo global se necesita observabilidad adicional. Un fallo de confirmación de transacción puede dejar resultado incierto: el siguiente ciclo consulta el mes y evita duplicados, sin atribuir una creación no confirmada. No hay UI React nueva en esta tarea.
