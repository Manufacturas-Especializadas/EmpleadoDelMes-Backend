# Registro de postulaciones — Tarea 6A

Las tres rutas requieren Bearer válido, rol Sponsor y contraseña definitiva. Los controles actuales comprueban cuenta activa, bloqueo y security stamp; no se modifica la autenticación. Los casos de uso consultan además la cuenta actual, rol y vínculo con un empleado, incluida una nueva comprobación dentro de la transacción de creación.

## Contrato HTTP

- `GET /api/nominations/active-campaign`: devuelve `id` y `evaluationMonth` de la convocatoria que acepta capturas en ese instante. Si ninguna acepta, devuelve 409 con un mensaje en español. Esta consulta es informativa, no reserva el derecho a registrar una postulación.
- `GET /api/nominations/candidates?term=Luis&pageNumber=1&pageSize=20`: exige un término no vacío de hasta 100 caracteres, página positiva y tamaño entre 1 y 100. Reutiliza EmployeeService y la búsqueda parcial por nómina/nombre con paginación SQL, orden por nómina e ID y total. Los elementos contienen exclusivamente `id`, `payrollNumber` y `fullName`. No se devuelve RowVersion. Las nóminas conservan sus ceros iniciales. Un resultado puede incluir al propio patrocinador; la creación rechaza la autopostulación.
- `POST /api/nominations`: recibe únicamente `candidateEmployeeId`, `qualities` y `justification`. Por ejemplo, `qualities: 5` representa Teamwork y Commitment. Los campos desconocidos se rechazan con 400, incluidos CampaignId, SponsorEmployeeId, SponsorPayrollNumber, SponsorFullName y CreatedByUserId.

La creación responde 201 con ID, convocatoria, autor, instantáneas del patrocinador y candidato, cualidades, justificación, RegisteredAtUtc e IsCancelled=false. No se agrega una ruta de detalle o historial fuera de alcance. No se exponen credenciales, hashes ni tokens.

El controlador obtiene el identificador del usuario desde el principal autenticado. Infrastructure consulta ApplicationUser y resuelve su EmployeeId contra el catálogo. El dominio toma las instantáneas de esos empleados, valida la autopostulación, los flags y la justificación, y aplica su normalización existente (Trim). No se duplican esas reglas ni se alteran los valores del enum.

## Convocatoria y transacción

La selección se basa en `Campaign.CanAcceptNominations(now)` con TimeProvider, no en el mes actual ni en la convocatoria más reciente. Se respetan archivo, aperturas/cierres manuales y los límites automáticos `[apertura, cierre)`. Si se detectan dos convocatorias abiertas por un estado inconsistente, se rechaza la operación con 409; no se elige una arbitrariamente.

CampaignRepository y NominationRepository comparten `CampaignAdministrationLock`. El helper conserva exactamente el recurso de 5B: `EmpleadoDelMes.CampaignAdministration.v1`, modo Exclusive, propietario Transaction y espera de 15 segundos. No modifica el bootstrap ni crea un segundo recurso incompatible.

La inserción inicia una transacción ReadCommitted y obtiene ese bloqueo **antes** de leer cuenta, empleados y convocatorias. Application decide la aceptación con el instante actual después de esas lecturas. El bloqueo se mantiene durante SaveChanges y hasta commit/rollback:

- Si el administrador confirma el cierre primero, el Sponsor obtiene el bloqueo después, lee el periodo cerrado y no inserta.
- Si el Sponsor obtiene el bloqueo primero y acepta una postulación, el cierre espera a que termine esa operación. La postulación precede al cierre en el orden transaccional.

El instante de aceptación y registro UTC es el de la validación dentro del bloqueo, no el de la consulta GET ni necesariamente el instante posterior del commit. Los horarios automáticos se evalúan en ese instante. Las escrituras SQL directas que eludan el protocolo de bloqueo no forman parte de esta garantía.

## Unicidad y errores

No se depende de una consulta Exists. El índice único existente `(CampaignId, SponsorEmployeeId, CandidateEmployeeId)` decide ante inserciones duplicadas y conserva las combinaciones de filas anuladas. Los errores SQL 2601/2627 se traducen a 409 sin exponer el mensaje del proveedor. Distintos candidatos o distintos patrocinadores no colisionan por esa restricción.

Los fallos revierten la transacción y desasocian la entidad intentada. Respuestas: 400 para datos inválidos; 401 para sesión/cuenta inválida; 403 para permisos, contraseña temporal o vínculo de patrocinador inválido; 404 para candidato inexistente; 409 para ausencia de convocatoria abierta, duplicados o conflictos de concurrencia; 503 para fallos temporales de persistencia. Ningún GET crea, abre o modifica convocatorias.

## Verificación y alcance

Las pruebas utilizan gestores Identity y host HTTP reales con almacenes dobles. Cubren identidades, permisos, estados de cuenta, validaciones, instantáneas, búsqueda acotada, duplicados históricos, fallos sin escritura y un cierre concurrente coordinado mediante el mismo bloqueo simulado que la administración. No prueban físicamente `sp_getapplock`, transacciones ni índices SQL Server: falta ejecutar esas pruebas con conexiones independientes en un entorno autorizado.

Esta tarea no ejecuta operaciones contra bases reales ni habilita CampaignAutomation. No introduce correcciones, anulaciones HTTP, auditoría, preselección, opiniones o historial del Sponsor. Los archivos administrativos encontrados previamente en el repositorio local se conservaron sin intervenirlos.
