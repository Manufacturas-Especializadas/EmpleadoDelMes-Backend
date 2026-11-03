# Revisión integral administrativa — 7D

Endpoints de solo lectura, protegidos por la política Administrator y la validación de sesión existente:

- GET /api/admin/candidates/review?campaignId={guid}&pageNumber=1&pageSize=20&term=
- GET /api/admin/candidates/{campaignId}/{candidateEmployeeId}/review

Se reutilizan NominationResult, NominationError y NominationCandidateSearch. CandidateReviewService valida identificadores, búsqueda de hasta 100 caracteres, página positiva, tamaño entre 1 y 100 y desplazamiento sin overflow. CandidateReviewPage contiene Items, Total, PageNumber y PageSize; CandidateReviewItem incluye identidad del candidato, convocatoria, conteo válido y tres indicadores departamentales. No incluye justificaciones ni textos de opinión en el listado.

CandidateReviewDetail incluye convocatoria y mes evaluado, datos actuales del catálogo, IsPreselected, total válido, Nominations y Opinions. ReviewNomination conserva las instantáneas de nómina y nombre del patrocinador, cualidades, justificación y fecha. ReviewOpinion incluye OpinionSource, HasOpinion, Content y fechas; las fuentes se devuelven siempre en orden HumanResources, Safety, Quality. Los departamentos pendientes tienen HasOpinion=false y texto/fechas null. ImmediateSupervisor está excluido explícitamente. No se exponen identificadores de cuentas, credenciales ni tokens.

Ambas consultas exigen preselección activa y al menos una postulación no anulada en la misma convocatoria. No exigen periodos abiertos y admiten convocatorias archivadas. La ausencia o el contenido de las opiniones no altera la elegibilidad. Convocatoria inexistente o candidato no elegible: 404 con mensaje claro. Una convocatoria existente sin elegibles responde 200 con lista vacía. Parámetros inválidos: 400; sesión o autorización: 401/403; persistencia: 503 sin detalles SQL.

## Consultas

El repositorio usa AsNoTracking. El listado realiza tres consultas: existencia de convocatoria, total de candidatos filtrados y página. GROUP BY CandidateEmployeeId y COUNT se aplican únicamente a Nominations de la convocatoria con IsCancelled=false. Se unen los grupos con empleados preseleccionados, se filtra por nombre/nómina y se pagina en SQL por conteo descendente, nómina ascendente e ID ascendente. Los indicadores de opinión usan EXISTS por fuente, sin JOIN que multiplique candidatos ni consultas N+1. Se conservan los ceros iniciales de nómina.

El detalle usa cuatro consultas acotadas: mes de convocatoria, empleado preseleccionado, postulaciones válidas del candidato y hasta tres opiniones de ese mismo candidato/convocatoria. Las postulaciones se ordenan por RegisteredAtUtc descendente e ID descendente. Se devuelven todas las válidas solicitadas, sin truncamiento; su cantidad determina el total del detalle. Solo se completan en memoria las tres entradas de opinión pendientes. Las anuladas siguen disponibles en los endpoints administrativos existentes.

No se usan escrituras, SaveChanges, transacciones de actualización ni bloqueos administrativos. Las lecturas separadas no prometen una instantánea transaccional: cambios concurrentes pueden producir variación entre total/página o entre partes del detalle. La revisión informa el estado observado y no reserva elegibilidad.

## Limitaciones y validación

Una preselección histórica activa sin postulaciones válidas queda fuera de la revisión, sin modificar automáticamente IsSelected ni sus opiniones. Su política de consistencia permanece pendiente. No hay puntuaciones, decisiones automáticas ni ganadores; la selección final y la opinión del jefe inmediato se gestionan fuera de estos endpoints.

No se añadieron pruebas nuevas por decisión del proyecto. Quedan pendientes validación de traducción SQL, planes de ejecución, rendimiento de detalles con muchas postulaciones, collation y comportamiento bajo cambios concurrentes en un SQL Server autorizado. No se accedió a bases reales ni se generaron migraciones.
