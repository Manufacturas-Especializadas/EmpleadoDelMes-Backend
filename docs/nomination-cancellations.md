# Anulación administrativa — 6B.4

`POST /api/admin/nominations/{id}/cancel` exige la política Administrator y conserva los controles existentes de sesión. Recibe exclusivamente `version` (RowVersion Base64 de 8 bytes) y `reason`. Rechaza campos adicionales, incluido ActorUserId; el actor procede del principal autenticado.

El motivo es obligatorio después de Trim y admite hasta 1,000 caracteres, compatible con la columna existente. La operación comprueba existencia, versión actual, estado no anulado y convocatoria no archivada. No exige que el periodo de postulaciones esté abierto.

Application llama a `Nomination.Cancel()` y construye una nueva entrada con acción Cancellation, ID propio, postulación, actor, fecha UTC de TimeProvider y motivo. Solo cambia IsCancelled: conserva identidades, convocatoria, instantáneas, cualidades, justificación, autor y fecha originales. No elimina registros ni auditorías anteriores.

## Transacción y concurrencia

Corrección y anulación comparten una implementación privada de persistencia en NominationAdminRepository. Cada operación abre una sola transacción, adquiere CampaignAdministrationLock antes de leer y compara RowVersion antes de mutar. El mismo bloqueo coordina la operación con el archivado administrativo. EF Core utiliza la versión del cliente como valor original para el UPDATE.

El UPDATE y el INSERT de auditoría se envían con un único SaveChanges y se confirman juntos. Los errores provocan rollback al disponer la transacción y se desasocian las entidades intentadas. No se utilizan transacciones anidadas. Una respuesta exitosa devuelve 200 con el detalle administrativo existente: `nomination.id`, `nomination.isCancelled=true`, los demás datos originales, `justification`, `createdByUserId` y `version` actualizada.

Una repetición devuelve 409 sin nueva auditoría: con versión antigua informa de concurrencia; con versión actual informa que ya está anulada. Las convocatorias archivadas también producen 409. Datos inválidos: 400; sesión inválida: 401; permisos insuficientes: 403; inexistente: 404; fallo de persistencia: 503. No se exponen errores del proveedor.

## Historial y preselecciones

El listado y detalle siguen mostrando registros anulados; el listado admite el filtro IsCancelled. El índice único de CampaignId, SponsorEmployeeId y CandidateEmployeeId no tiene filtro por anulación y sigue impidiendo reemplazar la postulación con otra igual. El caso de uso de corrección existente rechaza registros anulados.

Preselection se identifica por CampaignId y CandidateEmployeeId, sin FK directa a Nomination. CandidateOpinion se vincula a esa preselección. No se consultaron datos reales para determinar si existen filas asociadas. Esta operación no modifica preselecciones ni opiniones. La fase correspondiente deberá resolver expresamente el caso de un candidato preseleccionado cuya última postulación válida haya sido anulada, conservando sus opiniones; no se aplica una retirada automática.

No se agregaron pruebas automatizadas por decisión del proyecto. La suite existente verifica regresiones, no acredita físicamente este flujo nuevo. Queda pendiente validar en un SQL Server autorizado la devolución de RowVersion, solicitudes concurrentes, coordinación con archivado y rollback completo ante fallo de auditoría o actualización. No se ejecutaron operaciones contra bases reales ni se generaron migraciones.
