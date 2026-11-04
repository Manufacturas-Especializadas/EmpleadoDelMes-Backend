# Creación manual de convocatorias

`POST /api/campaigns` requiere Bearer válido, cuenta activa, contraseña definitiva y política Administrator. Cuerpo JSON:

```json
{"evaluationMonth":"2026-10-01"}
```

EvaluationMonth es obligatorio y debe ser una fecha válida correspondiente al primer día de un mes. No se normaliza silenciosamente un día diferente. Se rechazan campos adicionales. Solo se permiten meses anteriores al mes local actual de America/Monterrey; el instante procede de TimeProvider y la conversión utiliza TimeZoneInfo, sin offset constante ni zona del servidor. No se aplica FirstEvaluationMonth de la automatización como límite a la creación manual.

Responde 201 Created con el CampaignDto administrativo existente y Location `/api/campaigns/{id}`. La convocatoria tiene GUID nuevo, CreatedAtUtc, IsArchived=false, ambos periodos ManuallyClosed y horarios null. No se abre ningún periodo automáticamente.

HTTP: 400 fecha ausente, inválida, día distinto de 1 o mes actual/futuro; 401 sin sesión; 403 sin rol Administrator o con contraseña temporal; 409 mes duplicado, incluso archivado; 503 error de persistencia. El mensaje de duplicado es "Ya existe una convocatoria para ese mes evaluado.".

CampaignService comparte CreateMonthAsync entre CreateManualAsync y EnsurePreviousMonthAsync. Una consulta previa permite detectar duplicados, pero la garantía final es el índice único SQL Server de EvaluationMonth y el manejo existente de errores 2601/2627 en CampaignRepository. Si la automatización gana la carrera, la creación manual responde 409; si la manual gana, la automática recupera la convocatoria existente. Nunca se modifican datos ni periodos de la convocatoria ganadora. No hay migraciones ni cambios en la automatización.

Las pruebas HTTP y de servicio verifican autorización, Location, fechas, estado inicial, duplicados, persistencia y carreras con un repositorio doble. No acreditan ejecución física del índice SQL Server. Se valida el caso UTC 1 de noviembre a las 01:00 cuando Monterrey aún está en octubre: octubre todavía se rechaza. Windows/Linux deben disponer de datos de zona horaria para America/Monterrey, como requiere el calendario de planta; no se utiliza fallback silencioso.

Se ejecutan build y tests en Release con -m:1 para evitar los archivos Debug bloqueados por la API local detectados durante la tarea anterior. No se detiene ni inicia la API real y no se ejecutan creaciones contra bases reales.
