# Opiniones del jefe inmediato — Fase 2

La preselección administrativa sigue siendo obligatoria para los cuatro orígenes. Las bandejas y la revisión integral exigen IsSelected=true y al menos una postulación no anulada en la misma convocatoria. El autor de una postulación no determina al evaluador. No se cambian las reglas departamentales ni el flujo de postulaciones.

## Vinculación histórica

CampaignEvaluators tiene PK (CampaignId, CandidateEmployeeId), FK NoAction a Preselections y FK NoAction a SponsorEmployeeAssignments. AssignmentId apunta a una asignación histórica inmutable respecto de sus empleados. LinkedAtUtc identifica el instante de vinculación. La ausencia de fila significa pendiente; no es necesario insertar una fila nula.

Al crear por primera vez la preselección se consulta la asignación activa y, si existe, se inserta la vinculación dentro de la misma transacción. Si no existe, queda pendiente. Retirar/reincorporar una preselección o cambiar asignaciones generales no cambia ni completa automáticamente la vinculación histórica. La identidad del evaluador procede del EmployeeId del patrocinador de esa asignación, aunque la relación general ya esté inactiva.

`POST /api/admin/preselections/{campaignId}/{candidateEmployeeId}/evaluator-link`, sin cuerpo, exige Administrator y completa explícitamente un pendiente o una preselección anterior a esta fase. Requiere convocatoria no archivada, preselección activa y postulación válida. Es idempotente: una vinculación existente no se sobrescribe. Devuelve CampaignId, CandidateEmployeeId, SponsorEmployeeId e IsPending. Si falta asignación general devuelve IsPending=true sin escribir. No acepta un evaluador arbitrario.

El orden de bloqueo es siempre CampaignAdministrationLock y luego SponsorAssignments.v1. Las operaciones generales de asignación solo toman el segundo. Esto coordina primera selección/vinculación con archivo, retiro, anulaciones y cambios de asignación sin introducir un orden inverso. Los vínculos son inmutables y la clave compuesta protege duplicados. Si hay una opinión ImmediateSupervisor antigua sin vínculo, se rechaza la vinculación con 409: requiere una política explícita de conciliación para no atribuir opiniones a otra persona.

## Endpoints Sponsor

| Método | Ruta | Propósito |
|---|---|---|
| GET | /api/supervisor-opinions/campaigns | Convocatorias con colaboradores vinculados y actualmente elegibles |
| GET | /api/supervisor-opinions/candidates?campaignId=... | Bandeja paginada, búsqueda y filtro hasOpinion |
| GET | /api/supervisor-opinions/{campaignId}/{candidateEmployeeId} | Opinión histórica propia o pendiente |
| PUT | /api/supervisor-opinions/{campaignId}/{candidateEmployeeId} | Crear (201) o editar (200) |

Paginación pageNumber=1, pageSize=20, máximo 100 y desplazamiento sin overflow. term hasta 100 caracteres; hasOpinion=false muestra pendientes, true completadas, omitido todas. Consultas SQL AsNoTracking, EXISTS y paginación, sin cargar catálogos completos. Las convocatorias incluyen archivadas para consulta; las bandejas nunca incluyen empleados no preseleccionados o sin postulaciones válidas. Una convocatoria sin candidatos propios devuelve página vacía.

PUT recibe `{"content":"Opinión cualitativa", "version":null}` para crear, y versión Base64 de 8 bytes para editar. No admite origen, evaluador ni autor del cliente. Se usa el usuario de la sesión y se vuelve a comprobar su cuenta activa, contraseña definitiva, rol Sponsor y EmployeeId contra la vinculación. El middleware existente mantiene bloqueo y security stamp. Un patrocinador puede ser candidato evaluado por otro, sin que eso le permita opinar sobre sí mismo.

Se reutilizan CandidateOpinion, EditContent, el índice único y la transacción de OpinionRepository. Tras adquirir CampaignAdministrationLock se valida el evaluador, preselección activa, postulación válida y Campaign.CanAcceptOpinions(TimeProvider.GetUtcNow()). No se usa el periodo de postulaciones. Cierre, archivo, retiro y anulación no pueden intercalarse con la decisión de escritura. Las ediciones conservan CreatedByUserId/CreatedAtUtc, actualizan LastModifiedAtUtc y exigen RowVersion. Sin versión sobre un registro existente o con versión antigua: 409. Contenido normalizado idéntico con versión vigente: 200 sin nueva fecha ni escritura. Texto obligatorio, máximo 500 caracteres.

Retirar preselección conserva vínculo y opinión; bloquea nuevas escrituras y oculta al candidato de la bandeja. El evaluador histórico conserva acceso GET directo a la opinión almacenada; no ve opiniones departamentales. Las reasignaciones generales no conceden acceso al nuevo patrocinador sobre convocatorias ya vinculadas. Desactivar la cuenta bloquea su sesión sin transferir la opinión a otro usuario.

HTTP: 200/201 éxito; 400 solicitud inválida; 401 sesión inválida; 403 evaluador no autorizado; 404 registro inexistente cuando corresponde; 409 periodo, elegibilidad o concurrencia; 503 persistencia. No se exponen errores SQL ni datos de autenticación.

## Revisión administrativa y entrega

CandidateReviewItem agrega ImmediateSupervisorHasOpinion y HasEvaluator. CandidateReviewDetail agrega EvaluatorEmployeeId y una cuarta ReviewOpinion de fuente ImmediateSupervisor, pendiente con contenido/fechas null cuando no existe. Mantiene los filtros de preselección y postulaciones válidas. Las consultas históricas archivadas siguen disponibles.

Migración generada: 20261106202951_AddCampaignEvaluators; depende de AddSponsorEmployeeAssignments de Fase 1. No se aplicó ninguna. EF volvió a escribir el snapshot en el respaldo bajo obj; se copió su salida a la ubicación versionada de Persistence/Migrations. No se modificaron migraciones anteriores.

No se crearon ni ejecutaron tests. Se realizó una compilación Release sin errores que detectó una advertencia de nombre del DTO; se corrigió por revisión estática sin repetir la compilación. Los archivos de migración se generaron después con --no-build y se revisaron estáticamente. Pendientes: compilación de los artefactos generados en la próxima verificación autorizada; validación física de SQL, consultas, bloqueos, RowVersion y rollback; procedimiento aprobado para reasignar evaluadores históricos por bajas y para conciliar opiniones huérfanas. No hay endpoint para reemplazar vínculos existentes, sobrescribir opiniones ajenas ni completar pendientes desde GET.
