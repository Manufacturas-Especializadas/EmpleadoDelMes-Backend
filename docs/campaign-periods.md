# Administración de periodos — Tarea 5B

Todos los controles requieren Administrator, una sesión válida y haber cambiado la contraseña temporal. No se modifica el comportamiento de autenticación existente.

## Contrato HTTP

Consulta primero `GET /api/campaigns/{id}` y conserva `version` (RowVersion de SQL Server codificada en Base64, 8 bytes). El listado también incluye esa versión. Las respuestas contienen `canAcceptNominations` y `canAcceptOpinions`, calculados con TimeProvider al consultar; no son columnas persistidas y pueden cambiar con el tiempo sin cambiar la versión.

Programación:

- `PUT /api/campaigns/{id}/nominations/schedule`
- `PUT /api/campaigns/{id}/opinions/schedule`

Reciben JSON con `version`, `openingAt` y `closingAt`. Las fechas deben tener formato ISO 8601 con offset explícito, por ejemplo `2026-11-03T06:00:00-06:00` o `2026-11-03T12:00:00Z`. Se rechazan fechas sin offset, valores omitidos y apertura mayor o igual al cierre. Se guardan instantes UTC. No se interpreta la zona del servidor. Modificar un horario conserva el modo vigente. Las convocatorias archivadas rechazan cambios de horario desde el dominio.

Transiciones (reciben JSON con `version`):

- `POST /api/campaigns/{id}/nominations/open`
- `POST /api/campaigns/{id}/nominations/close`
- `POST /api/campaigns/{id}/nominations/automatic`
- `POST /api/campaigns/{id}/opinions/open`
- `POST /api/campaigns/{id}/opinions/close`
- `POST /api/campaigns/{id}/opinions/automatic`
- `POST /api/campaigns/{id}/archive`
- `POST /api/campaigns/{id}/reopen`

Cada éxito devuelve 200 y el DTO actualizado con su versión. Usa esa nueva versión para el siguiente cambio. Respuestas: 400 para solicitudes/horarios inválidos o transiciones prohibidas por archivo; 401 sin sesión; 403 sin permisos; 404 si no existe; 409 para versiones desactualizadas, conflictos de postulaciones o bloqueo concurrente; 503 ante fallos de persistencia. No se devuelven mensajes internos de SQL Server.

## Modos y repeticiones

Automatic requiere horarios configurados; permite capturas desde apertura inclusive hasta cierre exclusivo. ManuallyOpen tiene prioridad sobre las fechas y ManuallyClosed impide capturas aunque el horario esté vigente. El modo manual no caduca ni cambia al editar fechas.

Archivar cierra ambos periodos y conserva horarios e históricos. Reabrir una archivada conserva horarios, pero mantiene ambos periodos cerrados. Repetir archive sobre una archivada o reopen sobre una no archivada no hace cambios. Repetir un modo u horario idéntico tampoco modifica valores; la versión permanece si EF no detecta cambios. Reopen sobre una ya reabierta no cierra un periodo habilitado posteriormente. Siempre se exige una versión vigente: incluso una repetición con versión antigua devuelve 409.

## Exclusividad de postulaciones

Solo se consideran convocatorias no archivadas y periodos no cerrados manualmente:

- Dos aperturas manuales no pueden coexistir.
- Dos intervalos en Automatic no pueden solaparse. Se permiten ventanas adyacentes `[apertura, cierre)`; esta comprobación también rechaza solapamientos entre horarios automáticos históricos al editarlos.
- Una apertura manual no tiene final definido: impide activar cualquier intervalo automático que todavía no termine, incluso futuro. Una ventana automática vigente o futura impide abrir manualmente otra convocatoria. Un automático ya terminado no impide abrir manualmente.
- Un horario guardado en ManuallyClosed es una configuración inactiva, no una reserva. Se verifica su compatibilidad al habilitar Automatic. Editar fechas de un periodo Automatic vuelve a verificar los conflictos.
- No se cambia silenciosamente el modo de otra convocatoria. El administrador debe cerrar o ajustar explícitamente el periodo conflictivo.
- Las opiniones son independientes: pueden estar abiertas simultáneamente en varias convocatorias.

## Transacción y concurrencia

Cada modificación se ejecuta en una transacción ReadCommitted. Antes de leer la convocatoria y las demás ventanas, el repositorio solicita `sp_getapplock` exclusivo, propiedad de la transacción, con recurso `EmpleadoDelMes.CampaignAdministration.v1` y espera de hasta 15 segundos. El recurso es distinto del bootstrap. Se coordina también archivo, reapertura, cierres y cambios de opiniones para conservar un orden consistente de modificaciones.

Bajo ese bloqueo se leen datos actuales, se compara la versión del cliente, se aplica la operación del dominio y se valida la exclusividad desde Application. Solo entonces se llama a SaveChanges y se confirma la transacción. RowVersion se establece como valor original para que EF detecte también modificaciones externas concurrentes al guardar. Si cualquier comprobación o escritura falla, la transacción se revierte y la entidad modificada se desasocia del contexto; no se guardan cambios parciales.

La creación de 5A permanece independiente: solo inserta convocatorias cerradas, por lo que no introduce aperturas conflictivas. Toda futura vía que cambie los periodos debe respetar el mismo bloqueo; las escrituras SQL directas no quedan protegidas por esta coordinación cooperativa.

## Validación pendiente y Tarea 5C

Las pruebas unitarias y HTTP verifican reglas, conflictos simulados, reversión de cambios, versiones y permisos usando dobles. Las pruebas existentes de metadatos/materialización verifican RowVersion y cambios de CampaignPeriod sin conexión. Falta comprobar físicamente el bloqueo entre conexiones/instancias, los UPDATE con RowVersion y el rollback con SQL Server real en un entorno autorizado.

No se ejecutó SQL ni se cambiaron tablas o migraciones durante esta tarea. Para 5C quedan el huso horario de la planta y la generación mensual automática. No se agregaron procesos periódicos, captura de postulaciones u opiniones ni frontend.
